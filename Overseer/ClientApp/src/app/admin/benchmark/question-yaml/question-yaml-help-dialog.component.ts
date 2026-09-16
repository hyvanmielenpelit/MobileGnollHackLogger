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
  HUMAN_GUIDE_TABS,
  HumanGuideTab
} from './question-yaml-format';

const STATUS_MS = 3000;

export type YamlHelpTab = HumanGuideTab['id'] | 'ai';

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
  /** The dialog body: the single tab panel and the only scroller. */
  @ViewChild('body') body?: ElementRef<HTMLElement>;

  readonly guideTabs = HUMAN_GUIDE_TABS;
  /** Every tab in row order: the guide tabs, then the AI instructions. */
  readonly tabs: ReadonlyArray<{ id: YamlHelpTab; label: string }> = [
    ...HUMAN_GUIDE_TABS.map(t => ({ id: t.id, label: t.label })),
    { id: 'ai', label: 'For an AI' }
  ];
  activeTab: YamlHelpTab = 'workflow';

  readonly aiInstructions = AI_INSTRUCTIONS_MARKDOWN;

  copyStatus = '';
  private statusTimer: ReturnType<typeof setTimeout> | undefined;

  get activeGuide(): HumanGuideTab | null {
    return this.guideTabs.find(t => t.id === this.activeTab) ?? null;
  }

  open(): void {
    ensureOverlayPolyfills();
    const el = this.dialog?.nativeElement;
    if (el && !el.open) {
      el.showModal();
    }
    this.activeTab = 'workflow';
    this.copyStatus = '';
    this.cdr.detectChanges();
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  close(): void {
    this.dialog?.nativeElement?.close();
  }

  selectTab(tab: YamlHelpTab): void {
    this.activeTab = tab;
    this.cdr.detectChanges();
    // One scroller serves every tab, so a switch starts the new tab at the top.
    this.body?.nativeElement.scrollTo({ top: 0 });
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const count = this.tabs.length;
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % count;
    else if (event.key === 'ArrowLeft') next = (index - 1 + count) % count;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = count - 1;
    else return;

    event.preventDefault();
    this.selectTab(this.tabs[next].id);
    document.getElementById(`yaml-help-tab-${this.activeTab}`)?.focus();
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
