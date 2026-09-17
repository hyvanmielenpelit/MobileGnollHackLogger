import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  OnDestroy,
  OnInit,
  inject
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { copyToClipboard } from '../../utils/clipboard.util';
import { downloadTextFile } from '../../utils/download.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../utils/polyfills.util';

/** How long the copy button shows its check glyph. */
export const COPIED_MS = 2000;

/**
 * A code sample as a card: the exact text in `<pre><code>`, a Copy button in its top-right corner
 * and, with a caption, a header bar holding the caption and an optional Download button.
 *
 * The text is interpolated, never injected as HTML, so model or user text is safe here.
 */
@Component({
  selector: 'app-code-block',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './code-block.component.html',
  styleUrls: ['./code-block.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CodeBlockComponent implements OnInit, AfterViewInit, OnDestroy {
  private cdr = inject(ChangeDetectorRef);

  /** The text shown and copied. */
  @Input({ required: true }) code = '';
  /** Unique per instance; tooltip ids and anchor names derive from it. */
  @Input({ required: true }) idPrefix = '';
  /** Completes the accessible names: "Copy {subject} to the clipboard", "Download {subject}". */
  @Input({ required: true }) subject = '';
  /** A file name or title shown in a header bar; null draws no header. */
  @Input() caption: string | null = null;
  /** When set together with a caption, a Download button saves the text under this name. */
  @Input() downloadName: string | null = null;
  @Input() downloadType = 'application/yaml;charset=utf-8';

  copied = false;
  copyFailed = false;
  private copiedTimer: ReturnType<typeof setTimeout> | undefined;

  get canDownload(): boolean {
    return !!this.caption && !!this.downloadName;
  }

  get copyLabel(): string {
    return `Copy ${this.subject} to the clipboard`;
  }

  get downloadLabel(): string {
    return `Download ${this.subject}`;
  }

  get copyTipId(): string {
    return `tip-${this.idPrefix}-copy`;
  }

  get downloadTipId(): string {
    return `tip-${this.idPrefix}-download`;
  }

  get failureMessage(): string {
    return this.canDownload
      ? 'Could not copy; use Download instead.'
      : 'Could not copy; select the text instead.';
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngAfterViewInit(): void {
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  async copy(): Promise<void> {
    const ok = await copyToClipboard(this.code);
    clearTimeout(this.copiedTimer);
    this.copied = ok;
    this.copyFailed = !ok;
    this.cdr.markForCheck();
    if (ok) {
      this.copiedTimer = setTimeout(() => {
        this.copied = false;
        this.cdr.markForCheck();
      }, COPIED_MS);
    }
  }

  download(): void {
    if (!this.canDownload) return;
    downloadTextFile(this.downloadName!, this.code, this.downloadType);
  }

  ngOnDestroy(): void {
    clearTimeout(this.copiedTimer);
  }
}
