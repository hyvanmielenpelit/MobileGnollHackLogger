import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MarkdownPipe } from '../../../chat/markdown.pipe';
import { AdminBenchmarkService, RubricAuthoringGuidance } from '../../../services/admin-benchmark.service';
import { CodeBlockComponent } from '../../../shared/code-block/code-block.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import {
  AI_INGRESS,
  AI_INSTRUCTIONS_FILE_NAME,
  EXAMPLES_INGRESS,
  EXAMPLES_INTRO_MARKDOWN,
  GuideTab,
  HUMAN_GUIDE_TABS,
  YAML_EXAMPLES,
  YamlExample,
  buildAiInstructions,
  yamlExampleFileName
} from './question-yaml-format';
import {
  SUITE_AI_INGRESS,
  SUITE_EXAMPLES_INGRESS,
  SUITE_EXAMPLES_INTRO_MARKDOWN,
  SUITE_GUIDE_TABS,
  SUITE_YAML_EXAMPLES
} from './suite-yaml-guide';
import { GuideSegment, splitGuideMarkdown } from './guide-segments';
import { INSTRUCTIONS_FILE_NAME, WIZARD_LABELS, buildSuiteWorkflowInstructions } from './suite-workflow-instructions';

export type YamlHelpVariant = 'questions' | 'suite';
export type YamlHelpTab = GuideTab['id'] | 'examples' | 'ai';

@Component({
  selector: 'app-question-yaml-help-dialog',
  standalone: true,
  imports: [CommonModule, MarkdownPipe, CodeBlockComponent],
  templateUrl: './question-yaml-help-dialog.component.html',
  styleUrls: ['./question-yaml-help-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionYamlHelpDialogComponent {
  private cdr = inject(ChangeDetectorRef);
  private benchmarkService = inject(AdminBenchmarkService);

  @ViewChild('dialog') dialog!: ElementRef<HTMLDialogElement>;
  /** The dialog body: the single tab panel and the only scroller. */
  @ViewChild('body') body?: ElementRef<HTMLElement>;

  /**
   * Which help this instance is. Two instances share one document, so every element id carries
   * {@link idPrefix}; a duplicated id silently breaks `aria-labelledby`, `aria-controls`, the
   * tooltip anchors and the exclusive accordion.
   */
  @Input() variant: YamlHelpVariant = 'questions';

  /** The suite variant's request to open the Snapshot Suite Wizard; the host closes this help first. */
  @Output() wizardRequested = new EventEmitter<void>();

  readonly wizardLabel = `Open the ${WIZARD_LABELS.title}`;
  readonly yamlExampleFileName = yamlExampleFileName;
  readonly workflowFileName = INSTRUCTIONS_FILE_NAME;
  readonly workflowInstructions = buildSuiteWorkflowInstructions('both');
  private readonly segmentCache = new Map<string, GuideSegment[]>();

  activeTab: YamlHelpTab = 'workflow';

  /** The rubric guidance fetched from the server; null until it arrives, and when it failed. */
  guidance: RubricAuthoringGuidance | null = null;
  guidanceState: 'loading' | 'ready' | 'failed' = 'loading';

  get isSuite(): boolean {
    return this.variant === 'suite';
  }

  get idPrefix(): string {
    return this.isSuite ? 'suite-yaml-help' : 'yaml-help';
  }

  get title(): string {
    return this.isSuite ? 'Suite YAML Import and Export' : 'YAML Import and Export';
  }

  get closeLabel(): string {
    return this.isSuite ? 'Close suite YAML import and export help' : 'Close YAML import and export help';
  }

  get guideTabs(): ReadonlyArray<GuideTab> {
    return this.isSuite ? SUITE_GUIDE_TABS : HUMAN_GUIDE_TABS;
  }

  /** Every tab in row order: the guide tabs, the examples, then the AI Prompt. */
  get tabs(): ReadonlyArray<{ id: YamlHelpTab; label: string }> {
    return [
      ...this.guideTabs.map(t => ({ id: t.id as YamlHelpTab, label: t.label })),
      { id: 'examples' as YamlHelpTab, label: 'Examples' },
      { id: 'ai' as YamlHelpTab, label: 'AI Prompt' }
    ];
  }

  get examples(): ReadonlyArray<YamlExample> {
    return this.isSuite ? SUITE_YAML_EXAMPLES : YAML_EXAMPLES;
  }

  get examplesIntro(): string {
    return this.isSuite ? SUITE_EXAMPLES_INTRO_MARKDOWN : EXAMPLES_INTRO_MARKDOWN;
  }

  /** The questions variant's AI Prompt text; the suite variant's tab points at the wizard instead. */
  get aiInstructions(): string {
    return buildAiInstructions(this.guidance);
  }

  get aiFileName(): string {
    return AI_INSTRUCTIONS_FILE_NAME;
  }

  get aiHint(): string {
    return 'Paste these into an AI chat together with an exported document, so its reply imports cleanly.';
  }

  /** One or two plain-text sentences above the active tab's body. */
  get activeIngress(): string {
    if (this.activeTab === 'examples') {
      return this.isSuite ? SUITE_EXAMPLES_INGRESS : EXAMPLES_INGRESS;
    }
    if (this.activeTab === 'ai') {
      return this.isSuite ? SUITE_AI_INGRESS : AI_INGRESS;
    }
    return this.activeGuide?.ingress ?? '';
  }

  get activeGuide(): GuideTab | null {
    return this.guideTabs.find(t => t.id === this.activeTab) ?? null;
  }

  get activeTabLabel(): string {
    return this.tabs.find(t => t.id === this.activeTab)?.label ?? '';
  }

  /** The active guide tab split into prose and code, computed once per tab. */
  get activeGuideSegments(): GuideSegment[] {
    const guide = this.activeGuide;
    if (!guide) return [];
    const key = `${this.variant}:${guide.id}`;
    let segments = this.segmentCache.get(key);
    if (!segments) {
      segments = splitGuideMarkdown(guide.markdown);
      this.segmentCache.set(key, segments);
    }
    return segments;
  }

  /** The number of a code segment among the code segments of its tab, from 1. */
  codeNumber(segments: GuideSegment[], index: number): number {
    return segments.slice(0, index + 1).filter(s => s.kind === 'code').length;
  }

  open(): void {
    ensureOverlayPolyfills();
    const el = this.dialog?.nativeElement;
    if (el && !el.open) {
      el.showModal();
    }
    this.activeTab = 'workflow';
    this.loadGuidance();
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

  requestWizard(): void {
    this.wizardRequested.emit();
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const tabs = this.tabs;
    const count = tabs.length;
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % count;
    else if (event.key === 'ArrowLeft') next = (index - 1 + count) % count;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = count - 1;
    else return;

    event.preventDefault();
    this.selectTab(tabs[next].id);
    document.getElementById(`${this.idPrefix}-tab-${this.activeTab}`)?.focus();
  }

  /* Fetched once per component; a failed fetch is retried on the next open. The suite variant's
     text is static, so it never calls the server. */
  private loadGuidance(): void {
    if (this.isSuite || this.guidance) {
      return;
    }
    this.guidanceState = 'loading';
    this.benchmarkService.getRubricAuthoringGuidance().subscribe({
      next: guidance => {
        this.guidance = guidance;
        this.guidanceState = 'ready';
        this.cdr.detectChanges();
      },
      error: () => {
        this.guidanceState = 'failed';
        this.cdr.detectChanges();
      }
    });
  }
}
