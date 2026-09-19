import { Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminBenchmarkService, BenchmarkGameSnapshotDto, BoardFactsCheckDto } from '../../services/admin-benchmark.service';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { SnapshotTextEditorComponent } from './snapshot-text-editor.component';
import { DIGEST_MAX_CHARS, SnapshotDigestEditorComponent } from './snapshot-digest-editor.component';

const STATUS_MS = 2000;
const MODIFIED_AFTER_CREATE_MS = 60_000;
const TEXT_SAVED_STATUS = 'Saved. SHA-256 and digest updated.';

export type SnapshotSection = 'snapshot' | 'metadata' | 'delete';
export type UnsavedKind = 'text' | 'metadata';
/** What the strip under the tab row is asking: to close the dialog, or to revert both tabs. */
export type StripPrompt = 'close' | 'revert';

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
  @ViewChild('deleteConfirmDialog') deleteConfirmDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('deleteCancelButton') deleteCancelButton?: ElementRef<HTMLButtonElement>;
  @ViewChild('deleteSnapshotButton') deleteSnapshotButton?: ElementRef<HTMLButtonElement>;
  @ViewChild('viewerTitle') viewerTitle?: ElementRef<HTMLElement>;
  @ViewChild('keepEditingButton') keepEditingButton?: ElementRef<HTMLButtonElement>;
  @ViewChild('editNameInput') editNameInput?: ElementRef<HTMLInputElement>;
  @ViewChild(SnapshotTextEditorComponent) textEditor?: SnapshotTextEditorComponent;
  @ViewChild(SnapshotDigestEditorComponent) digestEditor?: SnapshotDigestEditorComponent;

  @Input() snapshotId: number | null = null;
  /** When set, Delete Snapshot is replaced by this explanation. */
  @Input() deleteBlockedReason: string | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() snapshotUpdated = new EventEmitter<BenchmarkGameSnapshotDto>();
  /** The id of a snapshot that was deleted; the viewer has closed. */
  @Output() snapshotDeleted = new EventEmitter<number>();

  snapshot: BenchmarkGameSnapshotDto | null = null;
  loading = false;
  error: string | null = null;
  copiedSha = false;

  /** Tab order for the row; rendered with @for, so an attribute is set in one place. */
  readonly sections: ReadonlyArray<{ id: SnapshotSection; label: string }> = [
    { id: 'snapshot', label: 'Game Snapshot' },
    { id: 'metadata', label: 'Metadata' },
    { id: 'delete', label: 'Delete' }
  ];
  activeTab: SnapshotSection = 'snapshot';

  editName = '';
  editNotes = '';
  editGnollHackVersion = '';
  editDigestText = '';
  savingEdit = false;
  regeneratingDigest = false;
  regenerateStatus = '';

  editingTextDirty = false;
  savingText = false;
  /** The footer's message for both tabs; an error names the part it concerns. */
  saveError: string | null = null;
  saveStatus: string | null = null;
  prompt: StripPrompt | null = null;
  /** The BOARD FACTS quote check from the most recent text save. Cleared on load and on reset. */
  boardFactsCheck: BoardFactsCheckDto | null = null;

  deleting = false;
  deleteError: string | null = null;

  /** Set by Save and download; cleared when the confirmation is dismissed first. */
  private downloadAfterSave = false;
  private copiedShaTimer: ReturnType<typeof setTimeout> | undefined;
  private saveStatusTimer: ReturnType<typeof setTimeout> | undefined;

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
    if (this.saving) return;
    if (this.guardUnsaved()) return;
    this.close();
  }

  /* Escape on the dialog. */
  onDialogCancel(event: Event) {
    if (this.saving || this.guardUnsaved()) {
      event.preventDefault();
      return;
    }
    this.close();
  }

  /* Clearing the snapshot destroys every panel and the CodeMirror instances inside them. */
  close() {
    this.closeDownloadConfirm();
    this.closeDeleteConfirm();
    this.resetState();
    this.snapshot = null;
    this.viewerDialog?.nativeElement?.close();
    this.closed.emit();
    this.cdr.detectChanges();
  }

  ngOnDestroy() {
    clearTimeout(this.copiedShaTimer);
    clearTimeout(this.saveStatusTimer);
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

  get carriageReturnCount(): number {
    const text = this.snapshot?.sanitizedText ?? '';
    let count = 0;
    for (let i = text.indexOf('\r'); i !== -1; i = text.indexOf('\r', i + 1)) count++;
    return count;
  }

  get hasCarriageReturns(): boolean {
    return this.carriageReturnCount > 0;
  }

  get canNormalizeLineEndings(): boolean {
    return this.hasCarriageReturns && !this.savingText && !this.editingTextDirty;
  }

  /* Saves the stored text unchanged; the server unifies its line endings, re-hashes it and rebuilds
     the digest. The editor buffer is not what is saved, so an unsaved edit blocks this. */
  normalizeLineEndings() {
    if (!this.snapshot?.sanitizedText || !this.canNormalizeLineEndings) return;
    this.saveText(this.snapshot.sanitizedText);
  }

  /* Shown only when the snapshot was changed after it was stored, not merely stamped at creation. */
  get showLastModified(): boolean {
    const modified = this.snapshot?.modifiedAtUtc ? Date.parse(this.snapshot.modifiedAtUtc) : NaN;
    const created = this.snapshot?.createdAtUtc ? Date.parse(this.snapshot.createdAtUtc) : NaN;
    return Number.isFinite(modified) && Number.isFinite(created) && modified - created > MODIFIED_AFTER_CREATE_MS;
  }

  // ---- Tabs ----------------------------------------------------------------------------------

  /* Every panel stays mounted, so switching never loses an edit and never asks. */
  selectTab(tab: SnapshotSection) {
    if (tab === this.activeTab) return;
    this.activeTab = tab;
    this.cdr.detectChanges();
    /* An editor cannot measure while its panel is display: none. */
    if (tab === 'snapshot') {
      this.textEditor?.refreshLayout();
    } else if (tab === 'metadata') {
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

  sectionDirty(section: SnapshotSection): boolean {
    if (section === 'snapshot') return this.editingTextDirty;
    if (section === 'metadata') return this.metadataDirty;
    return false;
  }

  // ---- Saving and reverting both tabs --------------------------------------------------------

  get saving(): boolean {
    return this.savingText || this.savingEdit;
  }

  get dirty(): boolean {
    return this.unsavedKinds.length > 0;
  }

  get unsavedKinds(): UnsavedKind[] {
    const kinds: UnsavedKind[] = [];
    if (this.editingTextDirty) kinds.push('text');
    if (this.metadataDirty) kinds.push('metadata');
    return kinds;
  }

  /** 'snapshot text', 'metadata', 'snapshot text and metadata', or '' when nothing is unsaved. */
  private get unsavedParts(): string {
    const kinds = this.unsavedKinds;
    if (kinds.length === 2) return 'snapshot text and metadata';
    if (kinds.length === 0) return '';
    return kinds[0] === 'metadata' ? 'metadata' : 'snapshot text';
  }

  get unsavedSummary(): string {
    return this.dirty ? `Unsaved changes: ${this.unsavedParts}` : '';
  }

  get unsavedLossText(): string {
    return `Unsaved changes to the ${this.unsavedParts} will be lost.`;
  }

  /* Text first, then metadata: the text save rebuilds the digest server-side, and the metadata
     save then writes the digest the form holds, so a hand-edited digest survives. The metadata is
     validated before either request, so text is never saved ahead of metadata that cannot follow. */
  saveAll() {
    if (!this.snapshot || !this.dirty || this.saving) return;
    if (this.metadataDirty) {
      const invalid = this.metadataValidationError();
      if (invalid) {
        this.showMetadataError(invalid.message, invalid.field);
        return;
      }
    }
    const text = this.editingTextDirty ? this.textEditor?.currentText() ?? null : null;
    if (text !== null) {
      this.saveText(text, () => {
        if (this.metadataDirty) this.saveEdit(true);
      });
    } else {
      this.saveEdit(false);
    }
  }

  /* Asks first when the revert would discard something not on screen. */
  revertAll() {
    if (!this.snapshot || !this.dirty || this.saving) return;
    const hiddenDirty = this.activeTab === 'delete'
      || (this.activeTab === 'snapshot' ? this.metadataDirty : this.editingTextDirty);
    if (hiddenDirty) {
      this.openPrompt('revert');
      return;
    }
    this.revertNow();
  }

  private revertNow() {
    if (!this.snapshot) return;
    if (this.editingTextDirty) this.textEditor?.revert();
    if (this.metadataDirty) this.initMetadataForm(this.snapshot);
    this.saveError = null;
    this.cdr.detectChanges();
  }

  private flashSaveStatus(message: string) {
    this.saveStatus = message;
    this.cdr.detectChanges();
    clearTimeout(this.saveStatusTimer);
    this.saveStatusTimer = setTimeout(() => {
      this.saveStatus = null;
      this.cdr.detectChanges();
    }, STATUS_MS);
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

  /* The rebuilt digest is saved server-side, so it is put on the snapshot as well as in the field,
     and the field stays clean. */
  regenerateDigest() {
    if (!this.snapshot || this.regeneratingDigest) return;
    this.regeneratingDigest = true;
    this.regenerateStatus = '';
    this.saveError = null;

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
        this.saveError = `Metadata: ${err?.error?.message || err?.error || 'Failed to regenerate the snapshot digest.'}`;
        this.cdr.detectChanges();
      }
    });
  }

  private metadataValidationError(): { message: string; field: 'name' | 'digest' } | null {
    if (!this.editName.trim()) {
      return { message: 'Metadata: Snapshot name is required.', field: 'name' };
    }
    /* The server truncates silently at this cap, so an over-long digest is refused here instead
       of being saved short. */
    if (this.editDigestText.trim().length > DIGEST_MAX_CHARS) {
      return { message: `Metadata: The digest is over ${DIGEST_MAX_CHARS.toLocaleString('en-US')} characters.`, field: 'digest' };
    }
    return null;
  }

  private showMetadataError(message: string, field: 'name' | 'digest') {
    this.saveError = message;
    this.saveStatus = null;
    this.selectTab('metadata');
    this.cdr.detectChanges();
    if (field === 'name') {
      this.editNameInput?.nativeElement.focus();
    } else {
      this.digestEditor?.view?.focus();
    }
  }

  /* afterText: the snapshot text was saved by the same Save Changes, which the messages say. */
  private saveEdit(afterText: boolean) {
    if (!this.snapshot || this.savingEdit || !this.metadataDirty) return;
    const textSaved = afterText ? 'Snapshot text saved. ' : '';
    const invalid = this.metadataValidationError();
    if (invalid) {
      this.showMetadataError(textSaved + invalid.message, invalid.field);
      return;
    }
    this.savingEdit = true;
    this.saveError = null;
    this.saveStatus = null;
    this.prompt = null;
    this.cdr.detectChanges();

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
        this.flashSaveStatus(afterText ? TEXT_SAVED_STATUS : 'Saved.');
      },
      error: (err) => {
        this.saveError = `${textSaved}Metadata: ${err?.error?.message || err?.error || 'Failed to update game snapshot.'}`;
        this.savingEdit = false;
        this.cdr.detectChanges();
      }
    });
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
    this.saveError = null;
    this.saveStatus = null;
    this.prompt = null;
    this.cdr.detectChanges();

    this.benchmarkService.updateSnapshotText(this.snapshot.id, {
      text,
      expectedSha256: this.snapshot.sha256
    }).subscribe({
      next: (response) => {
        const updated = response.snapshot;
        /* A digest edit in progress is kept; the metadata save that follows writes it over the
           rebuilt one. */
        const keepDigestEdit = this.digestDirty;
        this.snapshot = { ...this.snapshot!, ...updated, sanitizedText: updated.sanitizedText ?? text };
        if (!keepDigestEdit) this.editDigestText = this.snapshot.digestText || '';
        this.boardFactsCheck = response.boardFactsCheck;
        this.savingText = false;
        this.textEditor?.markSaved(this.snapshot.sanitizedText ?? '', text);
        this.snapshotUpdated.emit(this.snapshot);
        this.flashSaveStatus(TEXT_SAVED_STATUS);
        afterSave?.();
      },
      error: (err) => {
        this.savingText = false;
        const body = err?.error;
        const message = body?.error || body?.message || (typeof body === 'string' && body)
          || 'Failed to save the snapshot text.';
        this.saveError = `Snapshot text: ${message}`;
        this.downloadAfterSave = false;
        this.closeDownloadConfirm();
        this.cdr.detectChanges();
      }
    });
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

  // ---- Delete --------------------------------------------------------------------------------

  get deleteConsequenceText(): string {
    const suite = this.snapshot?.suiteName ? ` and detaches it from suite ${this.snapshot.suiteName}` : '';
    return `Deletes this snapshot permanently${suite}. Questions and runs are kept; runs keep the snapshot facts they recorded. `
      + 'Generate Questions and Check Rubrics need a snapshot, so they are unavailable for the suite until a new one is uploaded.';
  }

  requestDelete() {
    if (!this.snapshot || this.deleteBlockedReason || this.saving) return;
    this.deleteError = null;
    const dialog = this.deleteConfirmDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
      this.cdr.detectChanges();
      this.deleteCancelButton?.nativeElement.focus();
    }
  }

  /* The board is gone on success, so unsaved edits are not asked about. */
  confirmDelete() {
    if (!this.snapshot || this.deleting) return;
    const id = this.snapshot.id;
    this.deleting = true;
    this.deleteError = null;
    this.cdr.detectChanges();

    this.benchmarkService.deleteSnapshot(id).subscribe({
      next: () => {
        this.deleting = false;
        this.snapshotDeleted.emit(id);
        this.close();
      },
      error: (err) => {
        this.deleting = false;
        const body = err?.error;
        this.deleteError = body?.error || body?.message || (typeof body === 'string' && body)
          || 'Failed to delete the snapshot.';
        this.cdr.detectChanges();
      }
    });
  }

  closeDeleteConfirm() {
    if (this.deleting) return;
    const dialog = this.deleteConfirmDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  onDeleteConfirmCancel(event: Event) {
    if (this.deleting) {
      event.preventDefault();
      return;
    }
    this.deleteError = null;
  }

  // ---- The strip: close guard and revert prompt ----------------------------------------------

  get discardLabel(): string {
    const verb = this.prompt === 'revert' ? 'Revert' : 'Discard';
    return `${verb} unsaved changes to the ${this.unsavedParts}?`;
  }

  keepEditing() {
    this.prompt = null;
    this.cdr.detectChanges();
    this.focusActiveTab();
  }

  /* Discard closes the dialog; Revert restores both tabs and stays. */
  confirmPrompt() {
    if (this.prompt === 'revert') {
      this.prompt = null;
      this.revertNow();
      this.focusActiveTab();
    } else {
      this.discardAll();
    }
  }

  discardAll() {
    this.close();
  }

  private focusActiveTab() {
    if (this.activeTab === 'snapshot') {
      this.textEditor?.focus();
    } else if (this.activeTab === 'metadata') {
      this.editNameInput?.nativeElement.focus();
    } else {
      const target = this.deleteSnapshotButton?.nativeElement
        ?? document.getElementById(`snapshot-tab-delete-${this.uid}`);
      target?.focus();
    }
  }

  private openPrompt(prompt: StripPrompt) {
    this.prompt = prompt;
    this.cdr.detectChanges();
    this.keepEditingButton?.nativeElement.focus();
  }

  /* True when either tab holds unsaved changes; the discard prompt is then shown instead. */
  private guardUnsaved(): boolean {
    if (!this.dirty) return false;
    this.openPrompt('close');
    return true;
  }

  private resetState() {
    this.activeTab = 'snapshot';
    this.saveError = null;
    this.saveStatus = null;
    clearTimeout(this.saveStatusTimer);
    this.regenerateStatus = '';
    this.editingTextDirty = false;
    this.savingText = false;
    this.savingEdit = false;
    this.prompt = null;
    this.boardFactsCheck = null;
    this.downloadAfterSave = false;
    this.deleting = false;
    this.deleteError = null;
  }
}
