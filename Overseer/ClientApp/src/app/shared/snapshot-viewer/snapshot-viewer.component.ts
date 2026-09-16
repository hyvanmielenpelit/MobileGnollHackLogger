import { Component, ElementRef, EventEmitter, Input, NgZone, OnDestroy, Output, ViewChild, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminBenchmarkService, BenchmarkGameSnapshotDto } from '../../services/admin-benchmark.service';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { SnapshotTextEditorComponent } from './snapshot-text-editor.component';
import {
  FindMatch,
  MapBlock,
  ReaderChunk,
  ReaderSection,
  cellAt,
  cellOffset,
  detectMapBlock,
  detectSections,
  findMatches,
  formatWithLineNumbers,
  parseHeroPosition,
  splitIntoChunks,
  splitLines
} from './reader-text';

const READER_CHUNK_LINES = 100;
const FIND_DEBOUNCE_MS = 150;
const STATUS_MS = 2000;
const MODIFIED_AFTER_CREATE_MS = 60_000;
const PREF_LINE_NUMBERS = 'overseer.snapshotReader.lineNumbers';
const PREF_WRAP = 'overseer.snapshotReader.wrap';
const HIGHLIGHT_FIND = 'reader-find';
const HIGHLIGHT_FIND_CURRENT = 'reader-find-current';
const HIGHLIGHT_HERO = 'reader-hero';

interface MapCell {
  x: number;
  y: number;
  symbol: string;
}

export type SnapshotViewerTab = 'viewer' | 'editor' | 'metadata';

@Component({
  selector: 'app-snapshot-viewer',
  standalone: true,
  imports: [CommonModule, FormsModule, SnapshotTextEditorComponent],
  templateUrl: './snapshot-viewer.component.html',
  styleUrls: ['./snapshot-viewer.component.scss']
})
export class SnapshotViewerComponent implements OnDestroy {
  private static nextUid = 0;
  /* Keeps the title and tooltip ids unique when more than one viewer is on the page. */
  readonly uid = ++SnapshotViewerComponent.nextUid;

  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);
  private zone = inject(NgZone);

  @ViewChild('viewerDialog') viewerDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('viewerTitle') viewerTitle?: ElementRef<HTMLElement>;
  @ViewChild('keepEditingButton') keepEditingButton?: ElementRef<HTMLButtonElement>;
  @ViewChild(SnapshotTextEditorComponent) textEditor?: SnapshotTextEditorComponent;

  /* The reader region is re-created whenever the Viewer tab is shown, so listeners and
     highlight ranges follow the element rather than the component. */
  @ViewChild('readerScroll')
  set readerScrollRef(ref: ElementRef<HTMLElement> | undefined) {
    this.attachReaderScroll(ref?.nativeElement ?? null);
  }

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
  regeneratingDigest = false;
  regenerateStatus = '';

  /** Tab order for the row; rendered with @for, so an attribute is set in one place. */
  readonly tabs: ReadonlyArray<{ id: SnapshotViewerTab; label: string }> = [
    { id: 'viewer', label: 'Viewer' },
    { id: 'editor', label: 'Editor' },
    { id: 'metadata', label: 'Metadata' }
  ];
  activeTab: SnapshotViewerTab = 'viewer';
  /** Tab requested while the editor held unsaved changes; applied when those are discarded. */
  private pendingTab: SnapshotViewerTab | null = null;

  editingTextDirty = false;
  savingText = false;
  editTextError: string | null = null;
  showDiscardPrompt = false;

  lines: string[] = [];
  readerChunks: ReaderChunk[] = [];
  mapBlock: MapBlock | null = null;
  /** 1-based first line of the chunk holding the map block, or -1 without a map. */
  mapChunkStart = -1;
  sections: ReaderSection[] = [];
  lineCount = 0;
  lineNumberDigits = 2;

  showLineNumbers = this.readPref(PREF_LINE_NUMBERS, true);
  wrapLines = this.readPref(PREF_WRAP, false);

  goToLineInput: number | null = null;
  findQuery = '';
  matches: FindMatch[] = [];
  matchesTruncated = false;
  currentMatch = -1;
  targetLine: number | null = null;
  cellReadout = '';
  readerStatus = '';

  private searchedQuery = '';
  private heroPosition: { x: number; y: number } | null = null;
  private readerScrollEl: HTMLElement | null = null;
  private textNodes: Map<number, Text> | null = null;
  private findTimer: ReturnType<typeof setTimeout> | null = null;
  private targetTimer: ReturnType<typeof setTimeout> | undefined;
  private statusTimer: ReturnType<typeof setTimeout> | undefined;
  private pointerFrame = 0;
  private pendingPoint: { x: number; y: number } | null = null;

  /* Pointer tracking runs outside the Angular zone: a zone-triggered pass per pointermove would
     re-check the whole host page. Only this view is checked, and only when the readout changes. */
  private readonly onPointerMove = (event: PointerEvent) => {
    if (!this.mapBlock) return;
    this.pendingPoint = { x: event.clientX, y: event.clientY };
    if (this.pointerFrame) return;
    this.pointerFrame = requestAnimationFrame(() => {
      this.pointerFrame = 0;
      if (this.pendingPoint) this.updateCellReadout(this.pendingPoint.x, this.pendingPoint.y);
    });
  };

  private readonly onPointerLeave = () => {
    this.pendingPoint = null;
    this.clearCellReadout();
  };

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
    this.resetTextEditState();
    this.activeTab = 'viewer';
    this.loadSnapshot();
  }

  /* The header close button; unsaved text edits ask first. */
  requestClose() {
    if (this.guardUnsavedText()) return;
    this.close();
  }

  /* Escape on the dialog. */
  onDialogCancel(event: Event) {
    if (this.guardUnsavedText()) {
      event.preventDefault();
      return;
    }
    this.close();
  }

  close() {
    this.resetTextEditState();
    this.activeTab = 'viewer';
    this.clearHighlights();
    this.clearCellReadout();
    this.viewerDialog?.nativeElement?.close();
    this.closed.emit();
  }

  ngOnDestroy() {
    this.clearTimers();
    this.attachReaderScroll(null);
    this.clearHighlights();
  }

  loadSnapshot() {
    if (this.snapshotId == null) return;
    this.loading = true;
    this.error = null;
    this.benchmarkService.getSnapshot(this.snapshotId, true).subscribe({
      next: (data) => {
        this.snapshot = data;
        this.prepareReader(data.sanitizedText);
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
    this.regenerateStatus = '';
    this.isEditing = true;
  }

  cancelEdit() {
    this.isEditing = false;
    this.editError = null;
  }

  /* The rebuilt digest is saved server-side, so it is put on the snapshot as well as in the
     textarea: the disclosure then shows it even when the form is cancelled. */
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
    if (!this.snapshot) return;
    if (!this.editName.trim()) {
      this.editError = 'Snapshot name is required.';
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
        this.editError = err?.error?.message || err?.error || 'Failed to update game snapshot.';
        this.savingEdit = false;
        this.cdr.detectChanges();
      }
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

  get isEditingText(): boolean {
    return this.activeTab === 'editor';
  }

  /* Leaving the editor with unsaved changes shows the discard prompt instead; the requested tab
     is applied if the changes are discarded. */
  selectTab(tab: SnapshotViewerTab) {
    if (tab === this.activeTab || (this.isEditingText && this.savingText)) return;
    if (this.isEditingText && this.editingTextDirty) {
      this.pendingTab = tab;
      this.guardUnsavedText();
      return;
    }
    if (this.isEditingText) this.resetTextEditState();
    if (this.activeTab === 'metadata' && this.isEditing) this.cancelEdit();
    if (tab === 'editor') {
      this.clearCellReadout();
      this.clearHighlights();
    }
    this.activeTab = tab;
    this.cdr.detectChanges();
  }

  /* Arrow keys wrap, Home and End jump; selection follows focus. */
  onTabKeydown(event: KeyboardEvent, index: number) {
    const count = this.tabs.length;
    let nextIndex: number;
    switch (event.key) {
      case 'ArrowRight': nextIndex = (index + 1) % count; break;
      case 'ArrowLeft': nextIndex = (index - 1 + count) % count; break;
      case 'Home': nextIndex = 0; break;
      case 'End': nextIndex = count - 1; break;
      default: return;
    }
    event.preventDefault();
    const next = this.tabs[nextIndex].id;
    this.selectTab(next);
    if (this.activeTab !== next) return;
    document.getElementById(`snapshot-tab-${next}-${this.uid}`)?.focus();
  }

  // ---- Text editing --------------------------------------------------------------------------

  onTextDirtyChange(dirty: boolean) {
    this.editingTextDirty = dirty;
    if (!dirty) this.showDiscardPrompt = false;
    this.cdr.detectChanges();
  }

  /* The reader re-renders from the server's normalized copy, not from the editor's buffer. On
     failure the editor stays open with its buffer intact and shows the server's message. */
  saveText(text: string) {
    if (!this.snapshot || this.savingText) return;
    this.savingText = true;
    this.editTextError = null;
    this.showDiscardPrompt = false;
    this.cdr.detectChanges();

    this.benchmarkService.updateSnapshotText(this.snapshot.id, {
      text,
      expectedSha256: this.snapshot.sha256
    }).subscribe({
      next: (updated) => {
        this.snapshot = { ...this.snapshot!, ...updated, sanitizedText: updated.sanitizedText ?? text };
        this.prepareReader(this.snapshot.sanitizedText);
        this.resetTextEditState();
        this.activeTab = 'viewer';
        this.snapshotUpdated.emit(this.snapshot);
        this.flashStatus('Saved. SHA-256 and digest updated.');
        this.viewerTitle?.nativeElement.focus({ preventScroll: true });
      },
      error: (err) => {
        this.savingText = false;
        const body = err?.error;
        this.editTextError = body?.error || body?.message || (typeof body === 'string' && body)
          || 'Failed to save the snapshot text.';
        this.cdr.detectChanges();
      }
    });
  }

  cancelEditText() {
    if (this.savingText) return;
    if (this.guardUnsavedText()) return;
    this.resetTextEditState();
    this.activeTab = 'viewer';
    this.cdr.detectChanges();
  }

  keepEditingText() {
    this.showDiscardPrompt = false;
    this.pendingTab = null;
    this.cdr.detectChanges();
    this.textEditor?.focus();
  }

  discardTextEdits() {
    const target = this.pendingTab ?? 'viewer';
    this.resetTextEditState();
    this.activeTab = target;
    this.cdr.detectChanges();
  }

  /* True when the text editor holds unsaved changes; the discard prompt is then shown instead. */
  private guardUnsavedText(): boolean {
    if (!this.isEditingText || !this.editingTextDirty) return false;
    this.showDiscardPrompt = true;
    this.cdr.detectChanges();
    this.keepEditingButton?.nativeElement.focus();
    return true;
  }

  private resetTextEditState() {
    this.editingTextDirty = false;
    this.savingText = false;
    this.editTextError = null;
    this.showDiscardPrompt = false;
    this.pendingTab = null;
  }

  // ---- Reader: layout ------------------------------------------------------------------------

  isMapRow(lineNumber: number): boolean {
    return !!this.mapBlock?.yByLine.has(lineNumber - 1);
  }

  isRuler(lineNumber: number): boolean {
    const index = lineNumber - 1;
    return !!this.mapBlock && (index === this.mapBlock.tensRulerLine || index === this.mapBlock.unitsRulerLine);
  }

  isUnitsRuler(lineNumber: number): boolean {
    return !!this.mapBlock && lineNumber - 1 === this.mapBlock.unitsRulerLine;
  }

  get statusText(): string {
    return this.readerStatus || this.cellReadout;
  }

  get findStatus(): string {
    if (!this.searchedQuery) return '';
    if (this.matches.length === 0) return 'No matches';
    const total = this.matches.length.toLocaleString('en-US') + (this.matchesTruncated ? '+' : '');
    return `${this.currentMatch + 1} of ${total}`;
  }

  setShowLineNumbers(value: boolean) {
    this.showLineNumbers = value;
    this.writePref(PREF_LINE_NUMBERS, value);
  }

  setWrapLines(value: boolean) {
    this.wrapLines = value;
    this.writePref(PREF_WRAP, value);
  }

  private prepareReader(text: string | null | undefined) {
    this.clearHighlights();
    this.textNodes = null;
    this.lines = splitLines(text);
    this.lineCount = this.lines.length;
    this.lineNumberDigits = Math.max(2, String(this.lineCount).length);
    this.mapBlock = detectMapBlock(this.lines);
    /* The map block gets a chunk of its own, so the sticky ruler pins for exactly as long as
       a map row is on screen. */
    const breaks = this.mapBlock ? [this.mapBlock.headingLine, this.mapBlock.lastRowLine + 1] : [];
    this.readerChunks = splitIntoChunks(this.lines, READER_CHUNK_LINES, breaks);
    this.mapChunkStart = this.mapBlock ? this.mapBlock.headingLine + 1 : -1;
    this.sections = detectSections(this.lines);
    this.heroPosition = this.mapBlock ? parseHeroPosition(this.lines) : null;
    this.clearFindState();
    this.targetLine = null;
    this.cellReadout = '';
    this.readerStatus = '';
  }

  private attachReaderScroll(element: HTMLElement | null) {
    if (element === this.readerScrollEl) return;
    if (this.readerScrollEl) {
      this.readerScrollEl.removeEventListener('pointermove', this.onPointerMove);
      this.readerScrollEl.removeEventListener('pointerleave', this.onPointerLeave);
    }
    this.readerScrollEl = element;
    this.textNodes = null;
    if (!element) return;
    this.zone.runOutsideAngular(() => {
      element.addEventListener('pointermove', this.onPointerMove);
      element.addEventListener('pointerleave', this.onPointerLeave);
      /* The query setter runs inside a change-detection pass; the line text is in the DOM
         once that pass completes. */
      queueMicrotask(() => {
        if (this.readerScrollEl === element) this.registerHighlights();
      });
    });
  }

  // ---- Reader: navigation --------------------------------------------------------------------

  goToLine(lineNumber: number | null | undefined, match?: FindMatch) {
    if (lineNumber == null || this.lineCount === 0) return;
    const requested = Math.round(Number(lineNumber));
    if (!Number.isFinite(requested)) return;
    const line = Math.min(this.lineCount, Math.max(1, requested));

    this.targetLine = line;
    this.cdr.detectChanges();
    this.scrollToLine(line, match);

    clearTimeout(this.targetTimer);
    this.targetTimer = setTimeout(() => {
      this.targetLine = null;
      this.cdr.detectChanges();
    }, STATUS_MS);
  }

  onSectionChange(select: HTMLSelectElement) {
    const line = Number(select.value);
    select.value = '';
    if (line) this.goToLine(line);
  }

  private scrollToLine(lineNumber: number, match?: FindMatch) {
    const region = this.readerScrollEl;
    const lineEl = region?.querySelector<HTMLElement>(`[data-ln="${lineNumber}"]`);
    if (!region || !lineEl) return;

    const box = region.getBoundingClientRect();
    const lineBox = lineEl.getBoundingClientRect();
    const top = region.scrollTop + (lineBox.top - box.top) - (region.clientHeight - lineBox.height) / 2;

    let left = region.scrollLeft;
    const matchBox = match ? this.rangeFor(match.line, match.start, match.end)?.getBoundingClientRect() : undefined;
    if (matchBox) {
      const visibleLeft = box.left + region.clientLeft + this.gutterWidth(lineEl);
      const visibleRight = box.left + region.clientLeft + region.clientWidth;
      if (matchBox.left < visibleLeft || matchBox.right > visibleRight) {
        left = region.scrollLeft + (matchBox.left - visibleLeft) - 24;
      }
    }

    const reduceMotion = typeof window.matchMedia === 'function'
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    region.scrollTo({ top: Math.max(0, top), left: Math.max(0, left), behavior: reduceMotion ? 'auto' : 'smooth' });
  }

  // ---- Reader: find --------------------------------------------------------------------------

  onFindInput(value: string) {
    this.findQuery = value;
    if (this.findTimer) clearTimeout(this.findTimer);
    this.findTimer = setTimeout(() => this.runFind(), FIND_DEBOUNCE_MS);
  }

  onFindKeydown(event: KeyboardEvent) {
    if (event.key === 'Enter') {
      event.preventDefault();
      if (this.findTimer || this.searchedQuery !== this.findQuery) {
        this.runFind();
      } else {
        this.stepMatch(event.shiftKey ? -1 : 1);
      }
    } else if (event.key === 'Escape' && this.findQuery) {
      /* Escape clears a non-empty field; with the field empty it closes the dialog as usual. */
      event.preventDefault();
      event.stopPropagation();
      this.clearFind();
    }
  }

  runFind() {
    if (this.findTimer) {
      clearTimeout(this.findTimer);
      this.findTimer = null;
    }
    const result = findMatches(this.lines, this.findQuery);
    this.searchedQuery = this.findQuery;
    this.matches = result.matches;
    this.matchesTruncated = result.truncated;
    this.currentMatch = this.matches.length > 0 ? 0 : -1;
    this.registerFindHighlights();
    this.cdr.detectChanges();
    this.revealCurrentMatch();
  }

  stepMatch(delta: number) {
    const count = this.matches.length;
    if (count === 0) return;
    this.currentMatch = (this.currentMatch + delta + count) % count;
    this.updateCurrentMatchHighlight();
    this.revealCurrentMatch();
  }

  clearFind() {
    this.clearFindState();
    this.deleteHighlights(HIGHLIGHT_FIND, HIGHLIGHT_FIND_CURRENT);
    this.cdr.detectChanges();
  }

  private clearFindState() {
    if (this.findTimer) {
      clearTimeout(this.findTimer);
      this.findTimer = null;
    }
    this.findQuery = '';
    this.searchedQuery = '';
    this.matches = [];
    this.matchesTruncated = false;
    this.currentMatch = -1;
  }

  private revealCurrentMatch() {
    const match = this.matches[this.currentMatch];
    if (match) this.goToLine(match.line, match);
  }

  // ---- Reader: map tools ---------------------------------------------------------------------

  updateCellReadout(clientX: number, clientY: number) {
    const cell = this.cellAtPoint(clientX, clientY);
    this.setCellReadout(cell ? this.describeCell(cell) : '');
  }

  clearCellReadout() {
    if (this.pointerFrame) {
      cancelAnimationFrame(this.pointerFrame);
      this.pointerFrame = 0;
    }
    this.setCellReadout('');
  }

  onReaderClick(event: MouseEvent) {
    const selection = window.getSelection();
    if (selection && !selection.isCollapsed) return;

    const lineEl = (event.target as Element | null)?.closest?.('.reader-line') as HTMLElement | null;
    if (!lineEl || !this.readerScrollEl?.contains(lineEl)) return;
    const lineNumber = Number(lineEl.dataset['ln']);

    if (this.isGutterHit(lineEl, event.clientX)) {
      this.copyToClipboard(`L${lineNumber}`, `Copied L${lineNumber}`);
      return;
    }
    if (!this.mapBlock || !lineEl.classList.contains('map-row')) return;

    const cell = this.cellAtPoint(event.clientX, event.clientY);
    if (!cell) return;
    const coordinate = `<${cell.x},${cell.y}>`;
    this.copyToClipboard(coordinate, `Copied ${coordinate}`);
  }

  private setCellReadout(text: string) {
    if (text === this.cellReadout) return;
    this.cellReadout = text;
    this.cdr.detectChanges();
  }

  private describeCell(cell: MapCell): string {
    const coordinate = `<${cell.x},${cell.y}>`;
    return cell.symbol === ' '
      ? `${coordinate}  ' ' (blank: unseen or rock)`
      : `${coordinate}  '${cell.symbol}'`;
  }

  private cellAtPoint(clientX: number, clientY: number): MapCell | null {
    if (!this.mapBlock) return null;
    const caret = this.caretFromPoint(clientX, clientY);
    if (!caret || caret.node.nodeType !== Node.TEXT_NODE) return null;

    const lineEl = caret.node.parentElement?.closest('.reader-line.map-row') as HTMLElement | null;
    if (!lineEl || !this.readerScrollEl?.contains(lineEl)) return null;
    if (this.isGutterHit(lineEl, clientX)) return null;

    const lineNumber = Number(lineEl.dataset['ln']);
    const y = this.mapBlock.yByLine.get(lineNumber - 1);
    if (y === undefined) return null;

    const offset = this.charOffsetAt(caret.node as Text, caret.offset, clientX);
    const cell = cellAt(this.lines[lineNumber - 1] ?? '', offset);
    return cell ? { x: cell.x, y, symbol: cell.symbol } : null;
  }

  private caretFromPoint(x: number, y: number): { node: Node; offset: number } | null {
    const doc = document as Document & { caretRangeFromPoint?: (x: number, y: number) => Range | null };
    if (typeof doc.caretPositionFromPoint === 'function') {
      const position = doc.caretPositionFromPoint(x, y);
      return position ? { node: position.offsetNode, offset: position.offset } : null;
    }
    if (typeof doc.caretRangeFromPoint === 'function') {
      const range = doc.caretRangeFromPoint(x, y);
      return range ? { node: range.startContainer, offset: range.startOffset } : null;
    }
    return null;
  }

  /* A caret offset is a boundary between two characters; the character under the pointer is the
     one on the pointer's side of it. Past a row's trimmed end the cells are blank, one character
     width each. */
  private charOffsetAt(text: Text, caretOffset: number, clientX: number): number {
    const length = text.length;
    const offset = Math.max(0, Math.min(caretOffset, length));
    if (offset === 0) return 0;

    const range = document.createRange();
    range.setStart(text, offset - 1);
    range.setEnd(text, offset);
    const previous = range.getBoundingClientRect();
    if (clientX < previous.right) return offset - 1;
    if (offset === length && previous.width > 0) {
      return length + Math.floor((clientX - previous.right) / previous.width);
    }
    return offset;
  }

  /* The gutter is sticky, so it always sits at the region's left edge. */
  private isGutterHit(lineEl: HTMLElement, clientX: number): boolean {
    if (!this.showLineNumbers || !this.readerScrollEl) return false;
    const regionLeft = this.readerScrollEl.getBoundingClientRect().left + this.readerScrollEl.clientLeft;
    return clientX < regionLeft + this.gutterWidth(lineEl);
  }

  private gutterWidth(lineEl: HTMLElement): number {
    return this.showLineNumbers ? parseFloat(getComputedStyle(lineEl).paddingInlineStart) || 0 : 0;
  }

  // ---- Reader: copy --------------------------------------------------------------------------

  copyWithLineNumbers() {
    if (this.lineCount === 0) return;
    let from = 1;
    let to = this.lineCount;
    let fromSelection = false;

    const selection = window.getSelection();
    if (selection && !selection.isCollapsed && selection.rangeCount > 0) {
      const range = selection.getRangeAt(0);
      const start = this.lineNumberOf(range.startContainer);
      let end = this.lineNumberOf(range.endContainer);
      if (start !== null && end !== null) {
        /* A selection that ends at the very start of a line does not include that line. */
        if (end > start && range.endOffset === 0 && range.endContainer.nodeType === Node.TEXT_NODE) end--;
        from = Math.min(start, end);
        to = Math.max(start, end);
        fromSelection = true;
      }
    }

    const status = !fromSelection
      ? `Copied all ${to.toLocaleString('en-US')} lines`
      : from === to ? `Copied line ${from}` : `Copied lines ${from}–${to}`;
    this.copyToClipboard(formatWithLineNumbers(this.lines, from, to), status);
  }

  private lineNumberOf(node: Node): number | null {
    const element = node.nodeType === Node.TEXT_NODE ? node.parentElement : node as Element;
    const lineEl = element?.closest?.('.reader-line') as HTMLElement | null;
    if (!lineEl || !this.readerScrollEl?.contains(lineEl)) return null;
    const lineNumber = Number(lineEl.dataset['ln']);
    return Number.isFinite(lineNumber) ? lineNumber : null;
  }

  private copyToClipboard(text: string, status: string) {
    navigator.clipboard.writeText(text).then(
      () => this.flashStatus(status),
      () => this.flashStatus('Copy failed')
    );
  }

  private flashStatus(message: string) {
    this.readerStatus = message;
    this.cdr.detectChanges();
    clearTimeout(this.statusTimer);
    this.statusTimer = setTimeout(() => {
      this.readerStatus = '';
      this.cdr.detectChanges();
    }, STATUS_MS);
  }

  // ---- Reader: highlights (CSS Custom Highlight API) -----------------------------------------

  private get highlightsSupported(): boolean {
    return typeof CSS !== 'undefined' && 'highlights' in CSS && typeof Highlight !== 'undefined';
  }

  registerHighlights() {
    this.registerHeroHighlight();
    this.registerFindHighlights();
  }

  clearHighlights() {
    this.deleteHighlights(HIGHLIGHT_HERO, HIGHLIGHT_FIND, HIGHLIGHT_FIND_CURRENT);
  }

  private registerHeroHighlight() {
    if (!this.highlightsSupported) return;
    CSS.highlights.delete(HIGHLIGHT_HERO);
    if (!this.heroPosition || !this.mapBlock) return;
    const rowIndex = this.mapBlock.rowByY.get(this.heroPosition.y);
    if (rowIndex === undefined) return;
    const offset = cellOffset(this.heroPosition.x);
    const range = this.rangeFor(rowIndex + 1, offset, offset + 1);
    if (range) CSS.highlights.set(HIGHLIGHT_HERO, new Highlight(range));
  }

  private registerFindHighlights() {
    if (!this.highlightsSupported) return;
    CSS.highlights.delete(HIGHLIGHT_FIND);
    const ranges = this.matches
      .map(m => this.rangeFor(m.line, m.start, m.end))
      .filter((r): r is Range => r !== null);
    if (ranges.length > 0) CSS.highlights.set(HIGHLIGHT_FIND, new Highlight(...ranges));
    this.updateCurrentMatchHighlight();
  }

  private updateCurrentMatchHighlight() {
    if (!this.highlightsSupported) return;
    CSS.highlights.delete(HIGHLIGHT_FIND_CURRENT);
    const match = this.matches[this.currentMatch];
    const range = match ? this.rangeFor(match.line, match.start, match.end) : null;
    if (!range) return;
    const highlight = new Highlight(range);
    highlight.priority = 1;
    CSS.highlights.set(HIGHLIGHT_FIND_CURRENT, highlight);
  }

  private deleteHighlights(...names: string[]) {
    if (!this.highlightsSupported) return;
    for (const name of names) CSS.highlights.delete(name);
  }

  /* Each .reader-line holds exactly one text node: its line. A range in a chunk the browser has
     not rendered yet is still valid and is painted when the chunk renders. */
  private rangeFor(lineNumber: number, start: number, end: number): Range | null {
    const node = this.lineTextNode(lineNumber);
    if (!node || start < 0 || end > node.length || start >= end) return null;
    const range = document.createRange();
    range.setStart(node, start);
    range.setEnd(node, end);
    return range;
  }

  private lineTextNode(lineNumber: number): Text | null {
    if (!this.textNodes && this.readerScrollEl) {
      const nodes = new Map<number, Text>();
      this.readerScrollEl.querySelectorAll<HTMLElement>('.reader-line').forEach(lineEl => {
        const text = Array.from(lineEl.childNodes).find(n => n.nodeType === Node.TEXT_NODE) as Text | undefined;
        if (text) nodes.set(Number(lineEl.dataset['ln']), text);
      });
      this.textNodes = nodes;
    }
    return this.textNodes?.get(lineNumber) ?? null;
  }

  // ---- Preferences and timers ----------------------------------------------------------------

  private readPref(key: string, fallback: boolean): boolean {
    try {
      const value = localStorage.getItem(key);
      return value === null ? fallback : value === '1';
    } catch {
      return fallback;
    }
  }

  private writePref(key: string, value: boolean) {
    try {
      localStorage.setItem(key, value ? '1' : '0');
    } catch {
      /* Storage unavailable (private window, blocked site data): the choice lasts this session. */
    }
  }

  private clearTimers() {
    if (this.findTimer) clearTimeout(this.findTimer);
    this.findTimer = null;
    clearTimeout(this.targetTimer);
    clearTimeout(this.statusTimer);
    if (this.pointerFrame) cancelAnimationFrame(this.pointerFrame);
    this.pointerFrame = 0;
  }
}
