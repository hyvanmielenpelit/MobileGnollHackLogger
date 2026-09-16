import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  NgZone,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import type { EditorView } from '@codemirror/view';
import type { MapReadout, SnapshotDocInfo } from './codemirror-setup';
import { ReaderSection, detectSections, formatWithLineNumbers, splitLines } from './reader-text';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

/** The server's stored-text cap; longer text is cut and ends with the truncation marker. */
export const SNAPSHOT_MAX_CHARS = 60000;
const SECTIONS_DEBOUNCE_MS = 400;
const STATUS_MS = 2000;
export const PREF_LINE_NUMBERS = 'overseer.snapshotReader.lineNumbers';
export const PREF_WRAP = 'overseer.snapshotReader.wrap';

type CodeMirrorSetup = typeof import('./codemirror-setup');

/* The Game Snapshot page: a plain-text editor for a stored game snapshot with its copy, download
   and view tools. CodeMirror is loaded with import() on first mount, so it never reaches the
   initial bundle; only type imports of it appear here. */
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
  private zone = inject(NgZone);

  @ViewChild('editorHost', { static: true }) editorHost!: ElementRef<HTMLElement>;

  @Input() text = '';
  @Input() sha256 = '';
  @Input() snapshotName = '';
  @Input() saving = false;
  @Input() error: string | null = null;
  /** A transient success line from the host, shown in the footer when there is no error. */
  @Input() status: string | null = null;

  @Output() save = new EventEmitter<string>();
  @Output() download = new EventEmitter<{ dirty: boolean }>();
  @Output() dirtyChange = new EventEmitter<boolean>();

  /** Settles once the editor is mounted, or once loading it has failed. */
  readonly ready: Promise<void>;

  view: EditorView | null = null;
  loadError: string | null = null;
  dirty = false;
  length = 0;
  lineCount = 0;
  sections: ReaderSection[] = [];

  showLineNumbers = readPref(PREF_LINE_NUMBERS, true);
  wrapLines = readPref(PREF_WRAP, false);

  readout: MapReadout | null = null;
  copyStatus = '';
  copied = false;

  private setup: CodeMirrorSetup | null = null;
  private destroyed = false;
  private resolveReady!: () => void;
  private sectionsTimer: ReturnType<typeof setTimeout> | null = null;
  private statusTimer: ReturnType<typeof setTimeout> | null = null;
  private copiedTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.ready = new Promise<void>(resolve => (this.resolveReady = resolve));
  }

  get overCap(): boolean {
    return this.length > SNAPSHOT_MAX_CHARS;
  }

  get canSave(): boolean {
    return !!this.view && this.dirty && !this.saving;
  }

  get canRevert(): boolean {
    return !!this.view && this.dirty && !this.saving;
  }

  get coordinate(): string {
    return this.readout ? `<${this.readout.x},${this.readout.y}>` : '';
  }

  get statusText(): string {
    return this.copyStatus || this.readout?.text || '';
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
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
    for (const timer of [this.sectionsTimer, this.statusTimer, this.copiedTimer]) {
      if (timer) clearTimeout(timer);
    }
    this.sectionsTimer = this.statusTimer = this.copiedTimer = null;
    this.view?.destroy();
    this.view = null;
  }

  /** Returns keyboard focus to the editor content. */
  focus(): void {
    this.view?.focus();
  }

  /** Re-measures the editor after its panel was hidden. */
  refreshLayout(): void {
    this.view?.requestMeasure();
  }

  /** The current buffer, including unsaved changes; null before the editor has mounted. */
  currentText(): string | null {
    return this.setup && this.view ? this.setup.getDocText(this.view) : null;
  }

  /** Makes savedText the saved document. The buffer is replaced by it when it still holds
      submittedText, so edits typed while the save was in flight are kept and stay dirty. */
  markSaved(savedText: string, submittedText: string): void {
    if (!this.setup || !this.view) return;
    this.setup.markSaved(this.view, savedText, this.setup.getDocText(this.view) === submittedText);
  }

  requestSave(): void {
    if (!this.canSave || !this.setup || !this.view) return;
    this.save.emit(this.setup.getDocText(this.view));
  }

  revert(): void {
    if (!this.canRevert || !this.setup || !this.view) return;
    this.setup.revertToSaved(this.view);
  }

  requestDownload(): void {
    this.download.emit({ dirty: this.dirty });
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

  setShowLineNumbers(on: boolean): void {
    this.showLineNumbers = on;
    writePref(PREF_LINE_NUMBERS, on);
    if (this.setup && this.view) this.setup.setLineNumbers(this.view, on);
  }

  setWrapLines(on: boolean): void {
    this.wrapLines = on;
    writePref(PREF_WRAP, on);
    if (this.setup && this.view) this.setup.setLineWrapping(this.view, on);
  }

  copyText(): void {
    const text = this.currentText();
    if (text === null) return;
    this.writeClipboard(text).then(ok => {
      if (ok) {
        this.copied = true;
        if (this.copiedTimer) clearTimeout(this.copiedTimer);
        this.copiedTimer = setTimeout(() => {
          this.copiedTimer = null;
          this.copied = false;
          this.cdr.markForCheck();
        }, STATUS_MS);
      }
      this.flashStatus(ok ? 'Copied' : 'Copy failed');
    });
  }

  copyWithLineNumbers(): void {
    if (!this.setup || !this.view) return;
    const lines = splitLines(this.setup.getDocText(this.view));
    const range = this.setup.selectedLineRange(this.view);
    const from = range?.from ?? 1;
    const to = range?.to ?? lines.length;
    const status = !range
      ? `Copied all ${to.toLocaleString('en-US')} lines`
      : from === to ? `Copied line ${from}` : `Copied lines ${from}–${to}`;
    this.writeClipboard(formatWithLineNumbers(lines, from, to))
      .then(ok => this.flashStatus(ok ? status : 'Copy failed'));
  }

  copyCoordinate(): void {
    const coordinate = this.coordinate;
    if (!coordinate) return;
    this.writeClipboard(coordinate)
      .then(ok => this.flashStatus(ok ? `Copied ${coordinate}` : 'Copy failed'));
  }

  private writeClipboard(text: string): Promise<boolean> {
    /* navigator.clipboard is undefined outside a secure context. */
    const clipboard = typeof navigator !== 'undefined' ? navigator.clipboard : undefined;
    if (!clipboard) return Promise.resolve(false);
    return clipboard.writeText(text).then(() => true, () => false);
  }

  private flashStatus(message: string): void {
    this.copyStatus = message;
    this.cdr.markForCheck();
    if (this.statusTimer) clearTimeout(this.statusTimer);
    this.statusTimer = setTimeout(() => {
      this.statusTimer = null;
      this.copyStatus = '';
      this.cdr.markForCheck();
    }, STATUS_MS);
  }

  private async mount(text: string): Promise<void> {
    try {
      const setup = await import('./codemirror-setup');
      if (this.destroyed) return;
      this.setup = setup;
      /* CodeMirror's DOM listeners, pointermove among them, run outside the Angular zone; only the
         callbacks below re-enter it. */
      this.view = this.zone.runOutsideAngular(() => setup.createSnapshotEditor(
        this.editorHost.nativeElement,
        text,
        info => this.zone.run(() => this.onDocChanged(info)),
        {
          ariaLabel: 'Snapshot text',
          onSave: () => this.zone.run(() => this.requestSave()),
          lineNumbers: this.showLineNumbers,
          lineWrapping: this.wrapLines,
          mapTools: { onReadout: readout => this.zone.run(() => this.onReadout(readout)) }
        }
      ));
      const info = setup.getDocInfo(this.view);
      this.length = info.length;
      this.lineCount = info.lineCount;
    } catch {
      if (!this.destroyed) this.loadError = 'The text editor failed to load. Close it and try again.';
    } finally {
      if (!this.destroyed) this.cdr.markForCheck();
      this.resolveReady();
    }
  }

  private onReadout(readout: MapReadout | null): void {
    this.readout = readout;
    this.cdr.markForCheck();
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

function readPref(key: string, fallback: boolean): boolean {
  try {
    const value = localStorage.getItem(key);
    return value === null ? fallback : value === '1';
  } catch {
    return fallback;
  }
}

function writePref(key: string, value: boolean): void {
  try {
    localStorage.setItem(key, value ? '1' : '0');
  } catch {
    /* Storage unavailable (private window, blocked site data): the choice lasts this session. */
  }
}
