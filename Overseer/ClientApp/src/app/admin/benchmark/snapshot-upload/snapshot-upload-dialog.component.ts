import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  CaptureBenchmarkSnapshotResponse,
  SnapshotContentKind
} from '../../../services/admin-benchmark.service';
import { FilePickerComponent } from '../../../shared/file-picker/file-picker.component';
import { errorText } from '../question-yaml/question-yaml-import-dialog.component';

export const MAX_SNAPSHOT_FILE_BYTES = 4 * 1024 * 1024;
/** The server stores at most this many characters and marks the cut. */
export const SNAPSHOT_STORED_CHARS = 60_000;

/**
 * The server's rule for Auto: after trimming, content that starts with '<' and contains an html,
 * body or pre tag is an HTML dump; anything else is snapshot text.
 */
export function looksLikeHtml(content: string): boolean {
  const trimmed = (content ?? '').replace(/^﻿/, '').trim();
  if (!trimmed.startsWith('<')) return false;
  const lower = trimmed.toLowerCase();
  return lower.includes('<html') || lower.includes('<body') || lower.includes('<pre');
}

/** A file name without its extension and a trailing ".snapshot", as the viewer's download names it. */
export function snapshotNameFromFileName(fileName: string): string {
  return fileName
    .replace(/\.[^.]*$/, '')
    .replace(/\.snapshot$/i, '')
    .trim();
}

@Component({
  selector: 'app-snapshot-upload-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, FilePickerComponent],
  templateUrl: './snapshot-upload-dialog.component.html',
  styleUrls: ['./snapshot-upload-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SnapshotUploadDialogComponent implements OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('replaceConfirmDialog') replaceConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('confirmCancelButton') confirmCancelButton?: ElementRef<HTMLButtonElement>;

  @Output() uploaded = new EventEmitter<CaptureBenchmarkSnapshotResponse>();

  readonly maxStoredChars = SNAPSHOT_STORED_CHARS;

  suite: BenchmarkSuiteDto | null = null;
  /** When the current snapshot was captured; loaded for the replacement confirmation. */
  currentCapturedAt: string | null = null;

  content: string | null = null;
  fileName = '';
  fileError: string | null = null;
  /** The snapshot name last derived from a file name; `name` still equals it until the admin edits it. */
  private nameFromFile: string | null = null;

  contentKind: SnapshotContentKind = 'Auto';
  name = '';
  version = '';
  notes = '';

  posting = false;
  error: string | null = null;

  private currentSnapshotSub: Subscription | null = null;

  open(suite: BenchmarkSuiteDto): void {
    this.suite = suite;
    this.reset();
    const el = this.dialog?.nativeElement;
    if (el && !el.open) {
      el.showModal();
    }
    this.cdr.detectChanges();

    if (suite.gameSnapshotId) {
      this.currentSnapshotSub?.unsubscribe();
      this.currentSnapshotSub = this.benchmarkService.getSnapshot(suite.gameSnapshotId, false).subscribe({
        next: snapshot => {
          this.currentCapturedAt = snapshot.capturedAtUtc ?? snapshot.createdAtUtc ?? null;
          this.cdr.detectChanges();
        },
        error: () => { /* The date is optional in the confirmation text. */ }
      });
    }
  }

  close(): void {
    if (this.posting) return;
    this.closeReplaceConfirm();
    this.dialog?.nativeElement?.close();
  }

  onCancel(event: Event): void {
    if (this.posting) {
      event.preventDefault();
    }
  }

  ngOnDestroy(): void {
    this.currentSnapshotSub?.unsubscribe();
  }

  get hasCurrentSnapshot(): boolean {
    return !!this.suite?.gameSnapshotId;
  }

  get detectedIsHtml(): boolean {
    return this.content !== null && looksLikeHtml(this.content);
  }

  /** What the upload will be stored as, after the kind override. */
  get effectiveIsHtml(): boolean {
    return this.contentKind === 'Html' || (this.contentKind === 'Auto' && this.detectedIsHtml);
  }

  get canUpload(): boolean {
    return !this.posting && this.content !== null && this.name.trim() !== '';
  }

  /** What the file card shows under the name: the detected kind and the length. */
  get fileDetail(): string | null {
    if (this.content === null) return null;
    return `Detected: ${this.detectedIsHtml ? 'HTML dump' : 'snapshot text'}, ${this.content.length.toLocaleString('en-US')} characters`;
  }

  async loadFile(file: File): Promise<void> {
    this.fileError = null;
    this.error = null;
    if (file.size > MAX_SNAPSHOT_FILE_BYTES) {
      this.content = null;
      this.fileName = '';
      this.fileError = `${file.name} is larger than 4 MB.`;
      this.cdr.detectChanges();
      return;
    }
    try {
      this.content = await file.text();
      this.fileName = file.name;
      if (this.name.trim() === '' || this.name === this.nameFromFile) {
        this.name = snapshotNameFromFileName(file.name).slice(0, 128);
        this.nameFromFile = this.name;
      }
    } catch {
      this.content = null;
      this.fileError = `Could not read ${file.name}.`;
    }
    this.cdr.detectChanges();
  }

  /* A name derived from the file goes with it; a name the admin typed or edited stays. */
  clearFile(): void {
    this.content = null;
    this.fileName = '';
    this.fileError = null;
    this.error = null;
    if (this.nameFromFile !== null && this.name === this.nameFromFile) {
      this.name = '';
    }
    this.nameFromFile = null;
    this.cdr.detectChanges();
  }

  setContentKind(kind: SnapshotContentKind): void {
    this.contentKind = kind;
    this.cdr.detectChanges();
  }

  /* A suite with a snapshot asks first; nothing is sent until the replacement is confirmed. */
  upload(): void {
    if (!this.canUpload) return;
    if (this.hasCurrentSnapshot) {
      const confirm = this.replaceConfirmDialog?.nativeElement;
      if (confirm && !confirm.open) {
        confirm.showModal();
        this.cdr.detectChanges();
        this.confirmCancelButton?.nativeElement.focus();
      }
      return;
    }
    this.post(false);
  }

  confirmReplace(): void {
    if (!this.canUpload) return;
    this.closeReplaceConfirm();
    this.post(true);
  }

  closeReplaceConfirm(): void {
    const confirm = this.replaceConfirmDialog?.nativeElement;
    if (confirm?.open) confirm.close();
  }

  private post(replaceExisting: boolean): void {
    if (!this.suite || this.content === null) return;
    this.posting = true;
    this.error = null;
    this.cdr.detectChanges();

    this.benchmarkService.uploadSuiteSnapshot(this.suite.id, {
      name: this.name.trim(),
      content: this.content,
      contentKind: this.contentKind,
      notes: this.notes.trim() || null,
      sourceGnollHackVersion: this.version.trim() || null,
      replaceExisting
    }).subscribe({
      next: response => {
        this.posting = false;
        this.uploaded.emit(response);
        this.dialog?.nativeElement?.close();
        this.cdr.detectChanges();
      },
      error: err => {
        this.posting = false;
        this.error = errorText(err, 'The snapshot upload failed.');
        this.cdr.detectChanges();
      }
    });
  }

  private reset(): void {
    this.currentCapturedAt = null;
    this.content = null;
    this.fileName = '';
    this.fileError = null;
    this.contentKind = 'Auto';
    this.name = '';
    this.version = '';
    this.notes = '';
    this.posting = false;
    this.error = null;
    this.nameFromFile = null;
  }
}
