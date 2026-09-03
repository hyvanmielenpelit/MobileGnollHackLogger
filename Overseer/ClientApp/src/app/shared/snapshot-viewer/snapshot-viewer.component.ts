import { Component, ElementRef, EventEmitter, Input, Output, ViewChild, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminBenchmarkService, BenchmarkGameSnapshotDto } from '../../services/admin-benchmark.service';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

@Component({
  selector: 'app-snapshot-viewer',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './snapshot-viewer.component.html',
  styleUrls: ['./snapshot-viewer.component.scss']
})
export class SnapshotViewerComponent {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('viewerDialog') viewerDialog!: ElementRef<HTMLDialogElement>;

  @Input() snapshotId: number | null = null;
  @Output() closed = new EventEmitter<void>();
  @Output() snapshotUpdated = new EventEmitter<BenchmarkGameSnapshotDto>();

  snapshot: BenchmarkGameSnapshotDto | null = null;
  loading = false;
  error: string | null = null;
  copied = false;
  copiedSha = false;

  isEditing = false;
  editName = '';
  editNotes = '';
  editGnollHackVersion = '';
  editDigestText = '';
  savingEdit = false;
  editError: string | null = null;

  open(snapshotId?: number) {
    if (snapshotId != null) {
      this.snapshotId = snapshotId;
    }
    if (this.viewerDialog?.nativeElement) {
      ensureOverlayPolyfills();
      this.viewerDialog.nativeElement.showModal();
    }
    this.isEditing = false;
    this.editError = null;
    this.loadSnapshot();
  }

  close() {
    this.viewerDialog?.nativeElement?.close();
    this.closed.emit();
  }

  loadSnapshot() {
    if (this.snapshotId == null) return;
    this.loading = true;
    this.error = null;
    this.benchmarkService.getSnapshot(this.snapshotId, true).subscribe({
      next: (data) => {
        this.snapshot = data;
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = err?.error?.message || err?.error || 'Failed to load game board snapshot.';
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  copyText() {
    if (!this.snapshot?.sanitizedText) return;
    navigator.clipboard.writeText(this.snapshot.sanitizedText).then(() => {
      this.copied = true;
      this.cdr.detectChanges();
      setTimeout(() => {
        this.copied = false;
        this.cdr.detectChanges();
      }, 2000);
    });
  }

  copySha() {
    if (!this.snapshot?.sha256) return;
    navigator.clipboard.writeText(this.snapshot.sha256).then(() => {
      this.copiedSha = true;
      this.cdr.detectChanges();
      setTimeout(() => {
        this.copiedSha = false;
        this.cdr.detectChanges();
      }, 2000);
    });
  }

  downloadText() {
    if (this.snapshot?.id == null) return;
    const url = this.benchmarkService.getSnapshotTextUrl(this.snapshot.id);
    window.open(url, '_blank');
  }

  startEdit() {
    if (!this.snapshot) return;
    this.editName = this.snapshot.name;
    this.editNotes = this.snapshot.notes || '';
    this.editGnollHackVersion = this.snapshot.sourceGnollHackVersion || '';
    this.editDigestText = this.snapshot.digestText || '';
    this.editError = null;
    this.isEditing = true;
  }

  cancelEdit() {
    this.isEditing = false;
    this.editError = null;
  }

  saveEdit() {
    if (!this.snapshot) return;
    if (!this.editName.trim()) {
      this.editError = 'Board name is required.';
      return;
    }
    this.savingEdit = true;
    this.editError = null;

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
        this.savingEdit = false;
        this.isEditing = false;
        this.snapshotUpdated.emit(this.snapshot);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.editError = err?.error?.message || err?.error || 'Failed to update game board.';
        this.savingEdit = false;
        this.cdr.detectChanges();
      }
    });
  }

  get hasTruncationMarker(): boolean {
    return !!this.snapshot?.sanitizedText?.includes('[SNAPSHOT TRUNCATED');
  }
}
