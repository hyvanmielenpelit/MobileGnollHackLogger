import { Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminBenchmarkService, BenchmarkGameSnapshotDto } from '../../services/admin-benchmark.service';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { SnapshotTextEditorComponent } from './snapshot-text-editor.component';
import { DIGEST_MAX_CHARS, SnapshotDigestEditorComponent } from './snapshot-digest-editor.component';

const STATUS_MS = 2000;
const MODIFIED_AFTER_CREATE_MS = 60_000;

export type SnapshotSection = 'snapshot' | 'metadata';
export type UnsavedKind = 'text' | 'metadata';

@Component({
  selector: 'app-snapshot-viewer',
  standalone: true,
  imports: [CommonModule, FormsModule, SnapshotTextEditorComponent, SnapshotDigestEditorComponent],
  templateUrl: './snapshot-viewer.component.html',
  styleUrls: ['./snapshot-viewer.component.scss']
})
export class SnapshotViewerComponent implements OnDestroy {
  private static nextUid = 0;
  /* Keeps the title and tooltip ids unique when more than one viewer is on the page. */
  readonly uid = ++SnapshotViewerComponent.nextUid;

  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('viewerDialog') viewerDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('downloadConfirmDialog') downloadConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('viewerTitle') viewerTitle?: ElementRef<HTMLElement>;
  @ViewChild('keepEditingButton') keepEditingButton?: ElementRef<HTMLButtonElement>;
  @ViewChild('editNameInput') editNameInput?: ElementRef<HTMLInputElement>;
  @ViewChild(SnapshotTextEditorComponent) textEditor?: SnapshotTextEditorComponent;
  @ViewChild(SnapshotDigestEditorComponent) digestEditor?: SnapshotDigestEditorComponent;

  @Input() snapshotId: number | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() snapshotUpdated = new EventEmitter<BenchmarkGameSnapshotDto>();

  snapshot: BenchmarkGameSnapshotDto | null = null;
  loading = false;
  error: string | null = null;
  copiedSha = false;

  /** Tab order for the row; rendered with @for, so an attribute is set in one place. */
  readonly sections: ReadonlyArray<{ id: SnapshotSection; label: string }> = [
    { id: 'snapshot', label: 'Game Snapshot' },
    { id: 'metadata', label: 'Metadata' }
  ];
  activeTab: SnapshotSection = 'snapshot';

  editName = '';
  editNotes = '';
  editGnollHackVersion = '';
  editDigestText = '';
  savingEdit = false;
  editError: string | null = null;
  metadataStatus: string | null = null;
  regeneratingDigest = false;
  regenerateStatus = '';

  editingTextDirty = false;
  savingText = false;
  editTextError: string | null = null;
  textStatus: string | null = null;
  showDiscardPrompt = false;

  /** Set by Save and download; cleared when the confirmation is dismissed first. */
  private downloadAfterSave = false;
  private copiedShaTimer: ReturnType<typeof setTimeout> | undefined;
  private textStatusTimer: ReturnType<typeof setTimeout> | undefined;
  private metadataStatusTimer: ReturnType<typeof setTimeout> | undefined;

  open(snapshotId?: number) {
    if (snapshotId != null) {
      this.snapshotId = snapshotId;
    }
    if (this.viewerDialog?.nativeElement) {
      ensureOverlayPolyfills();
      this.viewerDialog.nativeElement.showModal();
    }
    this.resetState();
    /* Rendering without a snapshot first destroys any editors left from an earlier opening, so
       the new snapshot always gets freshly mounted ones. */
    this.snapshot = null;
    this.cdr.detectChanges();
    this.loadSnapshot();
  }

  /* The header close button; unsaved edits on either tab ask first. */
  requestClose() {
    if (this.savingText || this.savingEdit) return;
    if (this.guardUnsaved()) return;
    this.close();
  }

  /* Escape on the dialog. */
  onDialogCancel(event: Event) {
    if (this.savingText || this.savingEdit || this.guardUnsaved()) {
      event.preventDefault();
      return;
    }
    this.close();
  }

  /* Clearing the snapshot destroys both panels and the CodeMirror instances inside them. */
  close() {
    this.closeDownloadConfirm();
    this.resetState();
    this.snapshot = null;
    this.viewerDialog?.nativeElement?.close();
    this.closed.emit();
    this.cdr.detectChanges();
  }

  ngOnDestroy() {
    clearTimeout(this.copiedShaTimer);
    clearTimeout(this.textStatusTimer);
    clearTimeout(this.metadataStatusTimer);
  }

  loadSnapshot() {
    if (this.snapshotId == null) return;
    this.loading = true;
    this.error = null;
    this.benchmarkService.getSnapshot(this.snapshotId, true).subscribe({
      next: (data) => {
        this.snapshot = data;
        this.initMetadataForm(data);
        this.loading = false;
        this.cdr.detectChanges();
        this.viewerTitle?.nativeElement.focus({ preventScroll: true });
      },
      error: (err) => {
        this.error = err?.error?.message || err?.error || 'Failed to load game snapshot.';
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  copySha() {
    const sha = this.snapshot?.sha256;
    if (!sha || !navigator.clipboard) return;
    navigator.clipboard.writeText(sha).then(() => {
      this.copiedSha = true;
      this.cdr.detectChanges();
      clearTimeout(this.copiedShaTimer);
      this.copiedShaTimer = setTimeout(() => {
        this.copiedSha = false;
        this.cdr.detectChanges();
      }, STATUS_MS);
    });
  }

  get hasTruncationMarker(): boolean {
    return !!this.snapshot?.sanitizedText?.includes('[SNAPSHOT TRUNCATED');
  }

  /* Shown only when the snapshot was changed after it was stored, not merely stamped at creation. */
  get showLastModified(): boolean {
    const modified = this.snapshot?.modifiedAtUtc ? Date.parse(this.snapshot.modifiedAtUtc) : NaN;
    const created = this.snapshot?.createdAtUtc ? Date.parse(this.snapshot.createdAtUtc) : NaN;
    return Number.isFinite(modified) && Number.isFinite(created) && modified - created > MODIFIED_AFTER_CREATE_MS;
  }

  // ---- Tabs ----------------------------------------------------------------------------------

  /* Both panels stay mounted, so switching never loses an edit and never asks. */
  selectTab(tab: SnapshotSection) {
    if (tab === this.activeTab) return;
    this.activeTab = tab;
    this.cdr.detectChanges();
    /* An editor cannot measure while its panel is display: none. */
    if (tab === 'snapshot') {
      this.textEditor?.refreshLayout();
    } else {
      this.digestEditor?.view?.requestMeasure();
    }
  }

  /* Arrow keys wrap, Home and End jump; selection follows focus. */
  onTabKeydown(event: KeyboardEvent, index: number) {
    const count = this.sections.length;
    let nextIndex: number;
    switch (event.key) {
      case 'ArrowRight': nextIndex = (index + 1) % count; break;
      case 'ArrowLeft': nextIndex = (index - 1 + count) % count; break;
      case 'Home': nextIndex = 0; break;
      case 'End': nextIndex = count - 1; break;
      default: return;
    }
    event.preventDefault();
    const next = this.sections[nextIndex].id;
    this.selectTab(next);
    document.getElementById(`snapshot-tab-${next}-${this.uid}`)?.focus();
  }

  // ---- Metadata ------------------------------------------------------------------------------

  initMetadataForm(snapshot: BenchmarkGameSnapshotDto) {
    this.editName = snapshot.name;
    this.editGnollHackVersion = snapshot.sourceGnollHackVersion || '';
    this.editNotes = snapshot.notes || '';
    this.editDigestText = snapshot.digestText || '';
  }

  get metadataDirty(): boolean {
    const s = this.snapshot;
    if (!s) return false;
    return this.editName.trim() !== (s.name ?? '').trim()
      || this.editGnollHackVersion.trim() !== (s.sourceGnollHackVersion ?? '').trim()
      || this.editNotes.trim() !== (s.notes ?? '').trim()
      || this.digestDirty;
  }

  private get digestDirty(): boolean {
    return this.editDigestText.trim() !== (this.snapshot?.digestText ?? '').trim();
  }

  revertMetadata() {
    if (!this.snapshot || this.savingEdit || !this.metadataDirty) return;
    this.initMetadataForm(this.snapshot);
    this.editError = null;
    this.cdr.detectChanges();
  }

  /* The rebuilt digest is saved server-side, so it is put on the snapshot as well as in the field,
     and the field stays clean. */
  regenerateDigest() {
    if (!this.snapshot || this.regeneratingDigest) return;
    this.regeneratingDigest = true;
    this.regenerateStatus = '';
    this.editError = null;

    this.benchmarkService.regenerateSnapshotDigest(this.snapshot.id).subscribe({
      next: (updated) => {
        this.editDigestText = updated.digestText || '';
        this.snapshot = { ...this.snapshot!, digestText: updated.digestText };
        this.regeneratingDigest = false;
        this.regenerateStatus = `Digest rebuilt from the snapshot (${this.editDigestText.length} characters).`;
        this.snapshotUpdated.emit(this.snapshot);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.regeneratingDigest = false;
        this.regenerateStatus = '';
        this.editError = err?.error?.message || err?.error || 'Failed to regenerate the snapshot digest.';
        this.cdr.detectChanges();
      }
    });
  }

  saveEdit() {
    if (!this.snapshot || this.savingEdit || !this.metadataDirty) return;
    if (!this.editName.trim()) {
      this.editError = 'Snapshot name is required.';
      return;
    }
    /* The server truncates silently at this cap, so an over-long digest is refused here instead
       of being saved short. */
    if (this.editDigestText.trim().length > DIGEST_MAX_CHARS) {
      this.editError = `The digest is over ${DIGEST_MAX_CHARS.toLocaleString('en-US')} characters.`;
      return;
    }
    this.savingEdit = true;
    this.editError = null;
    this.metadataStatus = null;

    this.benchmarkService.updateSnapshot(this.snapshot.id, {
      name: this.editName.trim(),
      notes: this.editNotes.trim() || undefined,
      sourceGnollHackVersion: this.editGnollHackVersion.trim() || undefined,
      digestText: this.editDigestText.trim() || undefined
    }).subscribe({
      next: (updated) => {
        this.snapshot = {
          ...this.snapshot!,
          name: updated.name,
          notes: updated.notes,
          sourceGnollHackVersion: updated.sourceGnollHackVersion,
          digestText: updated.digestText
        };
        this.initMetadataForm(this.snapshot);
        this.savingEdit = false;
        this.snapshotUpdated.emit(this.snapshot);
        this.flashMetadataStatus('Saved.');
      },
      error: (err) => {
        this.editError = err?.error?.message || err?.error || 'Failed to update game snapshot.';
        this.savingEdit = false;
        this.cdr.detectChanges();
      }
    });
  }

  private flashMetadataStatus(message: string) {
    this.metadataStatus = message;
    this.cdr.detectChanges();
    clearTimeout(this.metadataStatusTimer);
    this.metadataStatusTimer = setTimeout(() => {
      this.metadataStatus = null;
      this.cdr.detectChanges();
    }, STATUS_MS);
  }

  // ---- Text editing --------------------------------------------------------------------------

  onTextDirtyChange(dirty: boolean) {
    this.editingTextDirty = dirty;
    this.cdr.detectChanges();
  }

  /* The editor stays on the tab. On success its saved document becomes the server's normalized
     copy; on failure its buffer is untouched and the footer shows the server's message. */
  saveText(text: string, afterSave?: () => void) {
    if (!this.snapshot || this.savingText) return;
    this.savingText = true;
    this.editTextError = null;
    this.textStatus = null;
    this.showDiscardPrompt = false;
    this.cdr.detectChanges();

    this.benchmarkService.updateSnapshotText(this.snapshot.id, {
      text,
      expectedSha256: this.snapshot.sha256
    }).subscribe({
      next: (updated) => {
        /* A digest edit in progress is kept; a later Save Changes overwrites the rebuilt one. */
        const keepDigestEdit = this.digestDirty;
        this.snapshot = { ...this.snapshot!, ...updated, sanitizedText: updated.sanitizedText ?? text };
        if (!keepDigestEdit) this.editDigestText = this.snapshot.digestText || '';
        this.savingText = false;
        this.textEditor?.markSaved(this.snapshot.sanitizedText ?? '', text);
        this.snapshotUpdated.emit(this.snapshot);
        this.flashTextStatus('Saved. SHA-256 and digest updated.');
        afterSave?.();
      },
      error: (err) => {
        this.savingText = false;
        const body = err?.error;
        this.editTextError = body?.error || body?.message || (typeof body === 'string' && body)
          || 'Failed to save the snapshot text.';
        this.downloadAfterSave = false;
        this.closeDownloadConfirm();
        this.cdr.detectChanges();
      }
    });
  }

  private flashTextStatus(message: string) {
    this.textStatus = message;
    this.cdr.detectChanges();
    clearTimeout(this.textStatusTimer);
    this.textStatusTimer = setTimeout(() => {
      this.textStatus = null;
      this.cdr.detectChanges();
    }, STATUS_MS);
  }

  // ---- Download ------------------------------------------------------------------------------

  /* The download always serves the saved text, so unsaved edits ask first. */
  downloadText() {
    if (!this.snapshot) return;
    if (!this.editingTextDirty) {
      this.startDownload();
      return;
    }
    const dialog = this.downloadConfirmDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
  }

  saveAndDownload() {
    if (this.savingText) return;
    const text = this.textEditor?.currentText();
    if (text == null) {
      this.closeDownloadConfirm();
      return;
    }
    this.downloadAfterSave = true;
    this.saveText(text, () => {
      if (!this.downloadAfterSave) return;
      this.downloadAfterSave = false;
      this.closeDownloadConfirm();
      this.startDownload();
    });
  }

  downloadSavedText() {
    this.closeDownloadConfirm();
    this.startDownload();
  }

  closeDownloadConfirm() {
    this.downloadAfterSave = false;
    const dialog = this.downloadConfirmDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  get downloadFileName(): string {
    const base = (this.snapshot?.name ?? '')
      .replace(/[^A-Za-z0-9._-]+/g, '_')
      .replace(/^[_.]+|[_.]+$/g, '');
    return `${base || 'snapshot'}.snapshot.txt`;
  }

  /* A same-origin link with a download attribute saves the file without opening a tab, and is not
     treated as a popup when it runs after an asynchronous save. */
  private startDownload() {
    if (!this.snapshot) return;
    const link = document.createElement('a');
    link.href = this.benchmarkService.getSnapshotTextUrl(this.snapshot.id);
    link.download = this.downloadFileName;
    link.hidden = true;
    const host = this.viewerDialog?.nativeElement ?? document.body;
    host.appendChild(link);
    link.click();
    link.remove();
  }

  // ---- Close guard ---------------------------------------------------------------------------

  get unsavedKinds(): UnsavedKind[] {
    const kinds: UnsavedKind[] = [];
    if (this.editingTextDirty) kinds.push('text');
    if (this.metadataDirty) kinds.push('metadata');
    return kinds;
  }

  get discardLabel(): string {
    const kinds = this.unsavedKinds;
    const subject = kinds.length === 2
      ? 'the snapshot text and metadata'
      : kinds[0] === 'metadata' ? 'the metadata' : 'the snapshot text';
    return `Discard unsaved changes to ${subject}?`;
  }

  keepEditing() {
    this.showDiscardPrompt = false;
    this.cdr.detectChanges();
    if (this.activeTab === 'snapshot') {
      this.textEditor?.focus();
    } else {
      this.editNameInput?.nativeElement.focus();
    }
  }

  discardAll() {
    this.close();
  }

  /* True when either tab holds unsaved changes; the discard prompt is then shown instead. */
  private guardUnsaved(): boolean {
    if (this.unsavedKinds.length === 0) return false;
    this.showDiscardPrompt = true;
    this.cdr.detectChanges();
    this.keepEditingButton?.nativeElement.focus();
    return true;
  }

  private resetState() {
    this.activeTab = 'snapshot';
    this.editError = null;
    this.metadataStatus = null;
    this.regenerateStatus = '';
    this.editingTextDirty = false;
    this.savingText = false;
    this.savingEdit = false;
    this.editTextError = null;
    this.textStatus = null;
    this.showDiscardPrompt = false;
    this.downloadAfterSave = false;
  }
}
