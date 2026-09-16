import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MarkdownPipe } from '../../../chat/markdown.pipe';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { downloadTextFile } from '../../../utils/download.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import {
  AI_INSTRUCTIONS_FILE_NAME,
  AI_INSTRUCTIONS_MARKDOWN,
  HUMAN_GUIDE_MARKDOWN
} from './question-yaml-format';

const STATUS_MS = 3000;

@Component({
  selector: 'app-question-yaml-help-dialog',
  standalone: true,
  imports: [CommonModule, MarkdownPipe],
  templateUrl: './question-yaml-help-dialog.component.html',
  styleUrls: ['./question-yaml-help-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionYamlHelpDialogComponent implements OnDestroy {
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') dialog!: ElementRef<HTMLDialogElement>;

  readonly guide = HUMAN_GUIDE_MARKDOWN;
  readonly aiInstructions = AI_INSTRUCTIONS_MARKDOWN;

  copyStatus = '';
  private statusTimer: ReturnType<typeof setTimeout> | undefined;

  open(): void {
    ensureOverlayPolyfills();
    const el = this.dialog?.nativeElement;
    if (el && !el.open) {
      el.showModal();
    }
    this.copyStatus = '';
    this.cdr.detectChanges();
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  close(): void {
    this.dialog?.nativeElement?.close();
  }

  async copyInstructions(): Promise<void> {
    const ok = await copyToClipboard(this.aiInstructions);
    this.flashStatus(ok ? 'Copied' : 'Could not copy; use Download instead.');
  }

  downloadInstructions(): void {
    downloadTextFile(AI_INSTRUCTIONS_FILE_NAME, this.aiInstructions, 'text/markdown;charset=utf-8');
  }

  ngOnDestroy(): void {
    clearTimeout(this.statusTimer);
  }

  private flashStatus(message: string): void {
    this.copyStatus = message;
    this.cdr.detectChanges();
    clearTimeout(this.statusTimer);
    this.statusTimer = setTimeout(() => {
      this.copyStatus = '';
      this.cdr.detectChanges();
    }, STATUS_MS);
  }
}
