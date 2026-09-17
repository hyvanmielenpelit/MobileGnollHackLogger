import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';

/** The extension tokens of an `accept` attribute (`.yaml`, `.yml`), lower-cased; MIME types are ignored. */
export function acceptExtensions(accept: string): string[] {
  return (accept ?? '')
    .split(',')
    .map(t => t.trim().toLowerCase())
    .filter(t => t.startsWith('.') && t.length > 1);
}

/** True when the name ends with one of the extensions, or when there are none to check. */
export function matchesAccept(fileName: string, accept: string): boolean {
  const extensions = acceptExtensions(accept);
  if (extensions.length === 0) return true;
  const lower = (fileName ?? '').trim().toLowerCase();
  return extensions.some(ext => lower.endsWith(ext));
}

function extensionList(extensions: string[]): string {
  if (extensions.length <= 1) return extensions.join('');
  return `${extensions.slice(0, -1).join(', ')} or ${extensions[extensions.length - 1]}`;
}

/**
 * A single-file picker: a labelled drop zone over a real, visually hidden `<input type="file">`
 * while empty, and a file card with a remove button once the host reports a file.
 *
 * Presentational only. The host reads the file, owns its state, and passes `fileName` back;
 * a non-null `fileName` is what switches the picker to its attached state.
 */
@Component({
  selector: 'app-file-picker',
  standalone: true,
  templateUrl: './file-picker.component.html',
  styleUrls: ['./file-picker.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FilePickerComponent implements OnInit, OnChanges, AfterViewChecked {
  private cdr = inject(ChangeDetectorRef);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The id of the native input; the label, card, tooltip and anchor names derive from it. */
  @Input({ required: true }) inputId!: string;
  @Input({ required: true }) label!: string;
  @Input() optional = false;
  @Input() accept = '';
  @Input() acceptHint = '';
  /** Extra ids appended to the input's `aria-describedby`. */
  @Input() describedBy: string | null = null;
  /** Non-null while a file is attached. */
  @Input() fileName: string | null = null;
  @Input() fileDetail: string | null = null;
  @Input() error: string | null = null;

  @Output() fileSelected = new EventEmitter<File>();
  @Output() cleared = new EventEmitter<void>();

  @ViewChild('fileInput') fileInput?: ElementRef<HTMLInputElement>;
  @ViewChild('card') card?: ElementRef<HTMLElement>;

  dragDepth = 0;
  status = '';
  /** A dropped file the `accept` extensions refuse; the native picker enforces `accept` itself. */
  dropError: string | null = null;

  private pendingFocus: 'card' | 'input' | null = null;
  private interacted = false;
  private droppedCount = 0;

  get attached(): boolean {
    return this.fileName !== null;
  }

  get shownError(): string | null {
    return this.error ?? this.dropError;
  }

  get describedByIds(): string | null {
    const ids = [
      this.acceptHint ? `${this.inputId}-accept` : null,
      this.describedBy,
      this.shownError ? `${this.inputId}-error` : null
    ].filter((id): id is string => !!id);
    return ids.length > 0 ? ids.join(' ') : null;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['fileName'];
    if (change && !change.firstChange && change.previousValue !== change.currentValue) {
      const wasAttached = change.previousValue !== null && change.previousValue !== undefined;
      const moveFocus = this.interacted || this.focusIsInside();
      this.interacted = false;

      if (this.fileName !== null) {
        this.dropError = null;
        const detail = this.fileDetail ? `, ${this.fileDetail}` : '';
        const extra = this.droppedCount > 1 ? ` Only the first of the ${this.droppedCount} dropped files was used.` : '';
        this.status = `${this.fileName} attached${detail}.${extra}`;
        if (moveFocus) this.pendingFocus = 'card';
      } else if (wasAttached) {
        this.status = 'File removed.';
        if (moveFocus) this.pendingFocus = 'input';
      }
      this.droppedCount = 0;
    }
    if (changes['error'] && this.error) {
      this.interacted = false;
      this.droppedCount = 0;
    }
  }

  ngAfterViewChecked(): void {
    const target = this.pendingFocus;
    if (!target) return;
    this.pendingFocus = null;
    if (target === 'card') {
      this.card?.nativeElement.focus();
      refreshAnchorPositioning();
    } else {
      this.fileInput?.nativeElement.focus();
    }
  }

  onNativeChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) {
      this.dropError = null;
      this.interacted = true;
      this.fileSelected.emit(file);
    }
    input.value = '';
  }

  clear(): void {
    this.interacted = true;
    this.dropError = null;
    this.cleared.emit();
  }

  onDragEnter(event: DragEvent): void {
    if (!this.carriesFiles(event)) return;
    event.preventDefault();
    this.dragDepth++;
  }

  onDragOver(event: DragEvent): void {
    if (!this.carriesFiles(event)) return;
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  }

  onDragLeave(event: DragEvent): void {
    if (!this.carriesFiles(event)) return;
    this.dragDepth = Math.max(0, this.dragDepth - 1);
  }

  onDrop(event: DragEvent): void {
    if (!this.carriesFiles(event)) return;
    event.preventDefault();
    this.dragDepth = 0;
    const files = event.dataTransfer?.files;
    const file = files?.[0];
    if (!file) return;

    if (!matchesAccept(file.name, this.accept)) {
      this.dropError = `${file.name} is not a ${extensionList(acceptExtensions(this.accept))} file.`;
      this.status = '';
      this.cdr.markForCheck();
      return;
    }
    this.dropError = null;
    this.droppedCount = files!.length;
    this.interacted = true;
    this.fileSelected.emit(file);
  }

  private carriesFiles(event: DragEvent): boolean {
    return Array.from(event.dataTransfer?.types ?? []).includes('Files');
  }

  private focusIsInside(): boolean {
    const active = document.activeElement;
    return !active || active === document.body || this.host.nativeElement.contains(active);
  }
}
