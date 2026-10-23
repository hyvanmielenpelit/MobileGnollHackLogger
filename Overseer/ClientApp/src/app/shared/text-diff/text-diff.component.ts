import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { DiffLine, DiffResult } from '../../utils/text-diff.util';

/** `side` shows the text before and after in two columns; `diff` shows one column of marked lines. */
export type TextDiffView = 'side' | 'diff';

/** The `+ `, `- ` or two-space gutter of a line in the one-column view. */
export function diffLinePrefix(line: DiffLine): string {
  return line.kind === 'added' ? '+ ' : line.kind === 'removed' ? '- ' : '  ';
}

/**
 * A line-level diff, rendered with the global `.import-diff` classes. In `side` the left column holds the
 * text before (equal and removed lines) and the right the text after (equal and added lines); in `diff` one
 * column holds every line with its `+` / `-` gutter. Text is interpolated, never rendered as HTML.
 */
@Component({
  selector: 'app-text-diff',
  standalone: true,
  templateUrl: './text-diff.component.html',
  styleUrls: ['./text-diff.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TextDiffComponent {
  private source: DiffResult = { lines: [], truncated: false };
  private beforeCache: DiffLine[] = [];
  private afterCache: DiffLine[] = [];

  @Input({ required: true })
  set diff(value: DiffResult) {
    this.source = value ?? { lines: [], truncated: false };
    this.beforeCache = this.source.lines.filter(line => line.kind !== 'added');
    this.afterCache = this.source.lines.filter(line => line.kind !== 'removed');
  }
  get diff(): DiffResult {
    return this.source;
  }

  @Input() view: TextDiffView = 'diff';
  /** The accessible names of the two columns of the side-by-side view. */
  @Input() beforeLabel = 'Before';
  @Input() afterLabel = 'After';

  readonly prefix = diffLinePrefix;

  get lines(): DiffLine[] {
    return this.source.lines;
  }

  get beforeLines(): DiffLine[] {
    return this.beforeCache;
  }

  get afterLines(): DiffLine[] {
    return this.afterCache;
  }
}
