import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import type { EditorView } from '@codemirror/view';
import type { SnapshotDocInfo } from './codemirror-setup';
import { ReaderSection, detectSections, splitLines } from './reader-text';

/** The server's stored-text cap; longer text is cut and ends with the truncation marker. */
export const SNAPSHOT_MAX_CHARS = 60000;
const SECTIONS_DEBOUNCE_MS = 400;

type CodeMirrorSetup = typeof import('./codemirror-setup');

/* A plain-text editor for a stored game snapshot. CodeMirror is loaded with import() on first
   mount, so it never reaches the initial bundle; only type imports of it appear here. */
@Component({
  selector: 'app-snapshot-text-editor',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './snapshot-text-editor.component.html',
  styleUrls: ['./snapshot-text-editor.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SnapshotTextEditorComponent implements OnInit, AfterViewInit, OnDestroy {
  private static nextUid = 0;
  readonly uid = ++SnapshotTextEditorComponent.nextUid;
  readonly maxChars = SNAPSHOT_MAX_CHARS;

  private cdr = inject(ChangeDetectorRef);

  @ViewChild('editorHost', { static: true }) editorHost!: ElementRef<HTMLElement>;

  @Input() text = '';
  @Input() sha256 = '';
  @Input() saving = false;
  @Input() error: string | null = null;

  @Output() save = new EventEmitter<string>();
  @Output() cancelled = new EventEmitter<void>();
  @Output() dirtyChange = new EventEmitter<boolean>();

  /** Settles once the editor is mounted, or once loading it has failed. */
  readonly ready: Promise<void>;

  view: EditorView | null = null;
  loadError: string | null = null;
  dirty = false;
  length = 0;
  lineCount = 0;
  sections: ReaderSection[] = [];

  private setup: CodeMirrorSetup | null = null;
  private destroyed = false;
  private resolveReady!: () => void;
  private sectionsTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.ready = new Promise<void>(resolve => (this.resolveReady = resolve));
  }

  get overCap(): boolean {
    return this.length > SNAPSHOT_MAX_CHARS;
  }

  get canSave(): boolean {
    return !!this.view && this.dirty && !this.saving;
  }

  ngOnInit(): void {
    const text = this.text ?? '';
    const lines = splitLines(text);
    this.length = text.length;
    this.lineCount = Math.max(1, lines.length);
    this.sections = detectSections(lines);
  }

  ngAfterViewInit(): void {
    void this.mount(this.text ?? '');
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.sectionsTimer) clearTimeout(this.sectionsTimer);
    this.sectionsTimer = null;
    this.view?.destroy();
    this.view = null;
  }

  /** Returns keyboard focus to the editor content. */
  focus(): void {
    this.view?.focus();
  }

  requestSave(): void {
    if (!this.canSave || !this.setup || !this.view) return;
    this.save.emit(this.setup.getDocText(this.view));
  }

  cancel(): void {
    this.cancelled.emit();
  }

  openFind(): void {
    if (!this.setup || !this.view) return;
    this.setup.openFind(this.view);
  }

  onSectionChange(select: HTMLSelectElement): void {
    const line = Number(select.value);
    select.value = '';
    if (line && this.setup && this.view) this.setup.scrollToLine(this.view, line);
  }

  private async mount(text: string): Promise<void> {
    try {
      const setup = await import('./codemirror-setup');
      if (this.destroyed) return;
      this.setup = setup;
      this.view = setup.createSnapshotEditor(
        this.editorHost.nativeElement,
        text,
        info => this.onDocChanged(info),
        { ariaLabel: 'Snapshot text', onSave: () => this.requestSave() }
      );
      const info = setup.getDocInfo(this.view);
      this.length = info.length;
      this.lineCount = info.lineCount;
      this.view.focus();
    } catch {
      if (!this.destroyed) this.loadError = 'The text editor failed to load. Close it and try again.';
    } finally {
      if (!this.destroyed) this.cdr.markForCheck();
      this.resolveReady();
    }
  }

  private onDocChanged(info: SnapshotDocInfo): void {
    this.length = info.length;
    this.lineCount = info.lineCount;
    if (info.modified !== this.dirty) {
      this.dirty = info.modified;
      this.dirtyChange.emit(this.dirty);
    }
    this.scheduleSectionsUpdate();
    this.cdr.markForCheck();
  }

  private scheduleSectionsUpdate(): void {
    if (this.sectionsTimer) clearTimeout(this.sectionsTimer);
    this.sectionsTimer = setTimeout(() => {
      this.sectionsTimer = null;
      if (!this.setup || !this.view) return;
      this.sections = detectSections(splitLines(this.setup.getDocText(this.view)));
      this.cdr.markForCheck();
    }, SECTIONS_DEBOUNCE_MS);
  }
}
