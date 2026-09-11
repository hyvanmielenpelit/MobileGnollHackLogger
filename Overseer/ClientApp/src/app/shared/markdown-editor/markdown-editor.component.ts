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
  ViewChild
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { MarkdownPipe } from '../../chat/markdown.pipe';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

export type MarkdownEditorMode = 'write' | 'split' | 'preview';

export interface MarkdownEditorWarning {
  id: string;
  message: string;
}

const FENCE_LINE = /^\s*(```|~~~)/;
const TABLE_ROW = /^\s*\|.*\|\s*$/;
const TABLE_SEPARATOR = /^\s*\|?[\s:-]*-[\s:|-]*$/;
const HEADING_NO_SPACE = /^\s*#{1,6}[^\s#]/;

/**
 * Structural Markdown warnings for `value`, fence-aware and capped at one
 * report per check regardless of how many lines trip it. Advisory only: the
 * caller decides what, if anything, to do with the result.
 */
export function computeMarkdownWarnings(value: string): MarkdownEditorWarning[] {
  const warnings: MarkdownEditorWarning[] = [];
  if (!value) {
    return warnings;
  }

  const lines = value.split('\n');

  const fenceLineCount = lines.filter(line => FENCE_LINE.test(line)).length;
  if (fenceLineCount % 2 !== 0) {
    warnings.push({
      id: 'fence',
      message: 'Unclosed code fence — everything after the last ``` is being treated as code.'
    });
  }

  // Tracks whether each line sits inside a fenced block, so the table and
  // heading checks below never fire on Markdown quoted inside an example.
  const insideFence: boolean[] = [];
  let inFence = false;
  for (const line of lines) {
    if (FENCE_LINE.test(line)) {
      insideFence.push(inFence);
      inFence = !inFence;
    } else {
      insideFence.push(inFence);
    }
  }

  let tableWarned = false;
  let headingWarned = false;
  for (let i = 0; i < lines.length; i++) {
    if (insideFence[i]) {
      continue;
    }
    const line = lines[i];

    // Only the row that opens a table is a header: every later row of a well-formed
    // table is a pipe row too, and its own successor is data rather than a separator.
    const opensTable =
      TABLE_ROW.test(line) &&
      (i === 0 || insideFence[i - 1] || !TABLE_ROW.test(lines[i - 1]));
    if (!tableWarned && opensTable) {
      const next = lines[i + 1];
      if (next === undefined || !TABLE_SEPARATOR.test(next)) {
        warnings.push({
          id: 'table',
          message: 'A table row has no --- separator line under its header, so it will render as plain text.'
        });
        tableWarned = true;
      }
    }

    if (!headingWarned && HEADING_NO_SPACE.test(line)) {
      warnings.push({ id: 'heading', message: 'A heading needs a space after the #.' });
      headingWarned = true;
    }
  }

  return warnings;
}

@Component({
  selector: 'app-markdown-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, MarkdownPipe],
  templateUrl: './markdown-editor.component.html',
  styleUrls: ['./markdown-editor.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The modifier belongs on the host, because the host is the flex child of whatever lays
  // the editor out; a class on the inner div is below the join and cannot grow anything.
  host: { '[class.md-editor-fill]': 'fill' }
})
export class MarkdownEditorComponent implements OnInit, AfterViewInit, OnChanges, OnDestroy {
  @Input({ required: true }) inputId!: string;
  @Input() label = '';
  @Input() hint = '';
  @Input() placeholder = '';
  @Input() minRows = 6;
  @Input() fill = false;
  @Input() splitMinWidth = 700;
  @Input() value = '';
  @Output() valueChange = new EventEmitter<string>();

  @ViewChild('root') private rootRef!: ElementRef<HTMLDivElement>;
  @ViewChild('editorTextarea') private textareaRef!: ElementRef<HTMLTextAreaElement>;

  mode: MarkdownEditorMode = 'write';
  /** Public so a test can drive it directly instead of forcing a ResizeObserver callback. */
  splitAvailable = true;
  warnings: MarkdownEditorWarning[] = [];

  private resizeObserver: ResizeObserver | null = null;
  private warningsTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(private readonly cdr: ChangeDetectorRef) {}

  get modes(): MarkdownEditorMode[] {
    return this.splitAvailable ? ['write', 'split', 'preview'] : ['write', 'preview'];
  }

  get describedBy(): string | null {
    const parts: string[] = [];
    if (this.hint) {
      parts.push(`${this.inputId}-hint`);
    }
    if (this.warnings.length > 0) {
      parts.push(`${this.inputId}-warnings`);
    }
    return parts.length > 0 ? parts.join(' ') : null;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.warnings = computeMarkdownWarnings(this.value);
  }

  ngAfterViewInit(): void {
    if (typeof ResizeObserver === 'undefined') {
      return;
    }
    this.resizeObserver = new ResizeObserver(entries => {
      const width = entries[0]?.contentRect.width ?? 0;
      const available = width >= this.splitMinWidth;
      if (available !== this.splitAvailable) {
        this.splitAvailable = available;
        if (!available && this.mode === 'split') {
          this.mode = 'write';
        }
        this.cdr.markForCheck();
      }
    });
    this.resizeObserver.observe(this.rootRef.nativeElement);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['value'] && !changes['value'].firstChange) {
      this.scheduleWarningsUpdate();
    }
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    if (this.warningsTimer !== null) {
      clearTimeout(this.warningsTimer);
    }
  }

  /** Returns the editor to Write mode, called imperatively by the host via ViewChild. */
  resetToWrite(): void {
    this.mode = 'write';
    this.cdr.markForCheck();
  }

  modeLabel(mode: MarkdownEditorMode): string {
    switch (mode) {
      case 'write': return 'Write';
      case 'split': return 'Split';
      case 'preview': return 'Preview';
    }
  }

  selectMode(mode: MarkdownEditorMode): void {
    this.mode = mode;
    this.cdr.markForCheck();
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const modes = this.modes;
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % modes.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + modes.length) % modes.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = modes.length - 1;
    else return;

    event.preventDefault();
    this.selectMode(modes[next]);
    document.getElementById(`${this.inputId}-tab-${this.mode}`)?.focus();
  }

  onValueChange(newValue: string): void {
    this.value = newValue;
    this.valueChange.emit(newValue);
    this.scheduleWarningsUpdate();
  }

  onEditorKeydown(event: KeyboardEvent): void {
    if (!event.ctrlKey && !event.metaKey) {
      return;
    }
    const key = event.key.toLowerCase();
    if (key === 'b') {
      event.preventDefault();
      this.applyBold();
    } else if (key === 'i') {
      event.preventDefault();
      this.applyItalic();
    }
  }

  applyBold(): void {
    this.toggleWrap('**');
  }

  applyItalic(): void {
    this.toggleWrap('*');
  }

  applyInlineCode(): void {
    this.toggleWrap('`');
  }

  applyBulletedList(): void {
    const ta = this.textareaRef.nativeElement;
    const start = ta.selectionStart ?? 0;
    const end = ta.selectionEnd ?? 0;
    const value = this.value;

    const lineStart = value.lastIndexOf('\n', start - 1) + 1;
    let lineEnd = value.indexOf('\n', end);
    if (lineEnd === -1) {
      lineEnd = value.length;
    }

    const block = value.slice(lineStart, lineEnd);
    const prefixed = block
      .split('\n')
      .map(line => (line.startsWith('- ') ? line : `- ${line}`))
      .join('\n');

    this.replaceRange(prefixed, lineStart, lineEnd);
    this.selectRange(lineStart, lineStart + prefixed.length);
  }

  applyTable(): void {
    const ta = this.textareaRef.nativeElement;
    const start = ta.selectionStart ?? this.value.length;
    const end = ta.selectionEnd ?? start;
    const skeleton = '| Header 1 | Header 2 | Header 3 |\n| --- | --- | --- |\n| Cell | Cell | Cell |\n| Cell | Cell | Cell |\n';

    this.replaceRange(skeleton, start, end);
    this.selectRange(start + skeleton.length, start + skeleton.length);
  }

  private toggleWrap(marker: string): void {
    const ta = this.textareaRef.nativeElement;
    const start = ta.selectionStart ?? 0;
    const end = ta.selectionEnd ?? 0;
    const value = this.value;
    const selected = value.slice(start, end);

    // The selection itself carries the markers.
    if (selected.length >= marker.length * 2 && selected.startsWith(marker) && selected.endsWith(marker)) {
      const inner = selected.slice(marker.length, selected.length - marker.length);
      this.replaceRange(inner, start, end);
      this.selectRange(start, start + inner.length);
      return;
    }

    // The markers sit immediately outside the selection.
    const before = value.slice(Math.max(0, start - marker.length), start);
    const after = value.slice(end, end + marker.length);
    if (before === marker && after === marker) {
      this.replaceRange(selected, start - marker.length, end + marker.length);
      this.selectRange(start - marker.length, start - marker.length + selected.length);
      return;
    }

    const wrapped = marker + selected + marker;
    this.replaceRange(wrapped, start, end);
    if (selected.length === 0) {
      this.selectRange(start + marker.length, start + marker.length);
    } else {
      this.selectRange(start, start + wrapped.length);
    }
  }

  private selectRange(start: number, end: number): void {
    const ta = this.textareaRef.nativeElement;
    ta.focus();
    ta.setSelectionRange(start, end);
  }

  private replaceRange(text: string, start: number, end: number): void {
    const ta = this.textareaRef.nativeElement;
    ta.focus();
    ta.setSelectionRange(start, end);
    try {
      if (document.execCommand('insertText', false, text)) {
        return;
      }
    } catch {
      /* not available: fall through */
    }
    ta.setRangeText(text, start, end, 'end');
    ta.dispatchEvent(new Event('input', { bubbles: true }));
  }

  private scheduleWarningsUpdate(): void {
    if (this.warningsTimer !== null) {
      clearTimeout(this.warningsTimer);
    }
    this.warningsTimer = setTimeout(() => {
      this.warningsTimer = null;
      this.warnings = computeMarkdownWarnings(this.value);
      this.cdr.markForCheck();
    }, 400);
  }
}
