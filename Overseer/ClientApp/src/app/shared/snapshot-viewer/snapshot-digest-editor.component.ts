import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { EditorView } from '@codemirror/view';
import type { SnapshotDocInfo } from './codemirror-setup';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

/** The server's stored-digest cap; a longer digest is truncated on save. */
export const DIGEST_MAX_CHARS = 6000;

type CodeMirrorSetup = typeof import('./codemirror-setup');

/* The snapshot digest as a form field: a framed CodeMirror editor with a character counter and a
   regenerate action. CodeMirror is loaded with import(), the same lazy chunk the text editor
   uses; a plain textarea stands in when that import fails, because this field sits inside a form
   the user still has to be able to save. */
@Component({
  selector: 'app-snapshot-digest-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './snapshot-digest-editor.component.html',
  styleUrls: ['./snapshot-digest-editor.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SnapshotDigestEditorComponent implements OnInit, AfterViewInit, OnChanges, OnDestroy {
  private static nextUid = 0;
  readonly uid = ++SnapshotDigestEditorComponent.nextUid;

  private cdr = inject(ChangeDetectorRef);

  @ViewChild('editorHost', { static: true }) editorHost!: ElementRef<HTMLElement>;

  @Input() text = '';
  @Input() maxChars = DIGEST_MAX_CHARS;
  @Input() regenerating = false;
  @Input() regenerateStatus = '';
  @Input() disabled = false;

  @Output() textChange = new EventEmitter<string>();
  @Output() regenerate = new EventEmitter<void>();

  /** Settles once the editor is mounted, or once loading it has failed. */
  readonly ready: Promise<void>;

  view: EditorView | null = null;
  loadError = false;
  length = 0;
  fallbackText = '';

  private setup: CodeMirrorSetup | null = null;
  private destroyed = false;
  private resolveReady!: () => void;

  constructor() {
    this.ready = new Promise<void>(resolve => (this.resolveReady = resolve));
  }

  get overCap(): boolean {
    return this.length > this.maxChars;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.fallbackText = this.text ?? '';
    this.length = this.fallbackText.length;
  }

  ngAfterViewInit(): void {
    void this.mount(this.text ?? '');
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['text']) return;
    const text = this.text ?? '';
    this.fallbackText = text;
    if (this.setup && this.view) {
      this.setup.replaceDocText(this.view, text);
    } else {
      this.length = text.length;
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.view?.destroy();
    this.view = null;
  }

  onRegenerateClick(): void {
    if (this.regenerating || this.disabled) return;
    this.regenerate.emit();
  }

  onFallbackInput(value: string): void {
    this.fallbackText = value;
    this.length = value.length;
    this.textChange.emit(value);
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
        { ariaLabel: 'Snapshot digest', lineWrapping: true }
      );
      this.length = setup.getDocInfo(this.view).length;
    } catch {
      if (!this.destroyed) this.loadError = true;
    } finally {
      if (!this.destroyed) this.cdr.markForCheck();
      this.resolveReady();
    }
  }

  private onDocChanged(info: SnapshotDocInfo): void {
    this.length = info.length;
    if (this.setup && this.view) this.textChange.emit(this.setup.getDocText(this.view));
    this.cdr.markForCheck();
  }
}
