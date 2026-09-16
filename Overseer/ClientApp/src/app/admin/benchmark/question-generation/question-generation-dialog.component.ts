import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
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

import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  QuestionGenerationJobDto,
  QuestionGenerationJobItemDto,
  RegenerateQuestionsScope
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { elapsedMsBetween } from '../../../utils/date.util';
import {
  formatDifficulty,
  formatServiceTier,
  formatThinkingLevel,
  showReasoningBadge
} from '../../../utils/model-badge-format.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { CollapsibleMarkdownComponent } from '../../../shared/collapsible-markdown/collapsible-markdown.component';

/** The display state of one job item; `partial` is a completed item that fell short of its count. */
export type QuestionGenerationItemState =
  | 'pending'
  | 'generating'
  | 'repairing'
  | 'completed'
  | 'partial'
  | 'failed'
  | 'skipped';

/** The operator instructions a new workspace starts with. */
export const DEFAULT_QUESTION_GENERATION_INSTRUCTIONS = `Write benchmark questions a GnollHack player would actually ask while looking at this exact game state. Each question must be unanswerable without the snapshot — if it could be answered from general GnollHack knowledge alone, it belongs in the knowledge suite, not here. Vary the decision type across questions; do not ask the same thing twice in different words. In each rubric, state only snapshot facts you can point to in the snapshot, and mark anything you infer as an inference.`;

/**
 * The question generation workspace: generator setup, live job progress with per-band retry, and
 * the suite's questions with their rubrics, reviewable and regenerable in place.
 *
 * The host owns `visible`. Every close path emits `closed`, carrying whether any question changed
 * so the host knows to reload; `questionsChanged` fires during a job, each time an item finishes.
 * Editing a question is handed to the host's own question form, which opens on top of this dialog.
 */
@Component({
  selector: 'app-question-generation-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ProviderBadgeComponent, CollapsibleMarkdownComponent],
  templateUrl: './question-generation-dialog.component.html',
  styleUrls: ['./question-generation-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionGenerationDialogComponent implements OnInit, OnChanges, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  static readonly POLL_INTERVAL_MS = 2000;
  static readonly COPIED_RESET_MS = 2000;

  @Input() suite: BenchmarkSuiteDto | null = null;
  @Input() visible = false;
  @Input() benchmarkCapableConfigs: SystemAiConfigDto[] = [];
  @Input() defaultModelConfigId: number | null = null;
  @Input() overseerBuildVersion: string | null = null;

  /** Escape, the close button, *Close* and *Run in Background*. */
  @Output() closed = new EventEmitter<{ questionsChanged: boolean }>();
  /** The suite id, each time a job item finishes. */
  @Output() questionsChanged = new EventEmitter<number>();
  /** The host opens its question form on top of this dialog. */
  @Output() editQuestionRequested = new EventEmitter<BenchmarkQuestionDto>();

  @ViewChild('dialog', { static: true }) dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('heading', { static: true }) heading?: ElementRef<HTMLElement>;
  @ViewChild('confirmDialog', { static: true }) confirmDialog?: ElementRef<HTMLDialogElement>;

  readonly formatThinkingLevel = formatThinkingLevel;
  readonly showReasoningBadge = showReasoningBadge;
  readonly formatServiceTier = formatServiceTier;
  readonly formatDifficulty = formatDifficulty;

  // --- Setup ---------------------------------------------------------------------------------
  modelConfigId: number | null = null;
  simpleCount = 6;
  intermediateCount = 6;
  advancedCount = 6;
  instructions = DEFAULT_QUESTION_GENERATION_INSTRUCTIONS;
  isModelDropdownOpen = false;

  // --- Job -----------------------------------------------------------------------------------
  job: QuestionGenerationJobDto | null = null;
  jobStarting = false;
  retrying = false;
  cancelling = false;
  dialogError: string | null = null;
  lastPollAtUtc: string | null = null;
  lastPollError: string | null = null;

  // --- Questions -----------------------------------------------------------------------------
  questions: BenchmarkQuestionDto[] = [];
  questionsLoading = false;
  selectedIds = new Set<number>();
  /** Questions a job created or rewrote since this dialog adopted it. */
  newIds = new Set<number>();
  anyQuestionsChanged = false;

  // --- Diagnostics ---------------------------------------------------------------------------
  diagnosticsOpen = false;
  copiedDiagnostics = false;
  diagnosticsCopyFailed = false;

  // --- Local confirmation --------------------------------------------------------------------
  confirmTitle = '';
  confirmMessage = '';
  confirmDangerNotice = '';
  confirmButtonText = 'Confirm';
  confirmButtonClass = 'btn-gh btn-gh-delete';
  private confirmAction: (() => void) | null = null;

  private isOpen = false;
  private jobId: string | null = null;
  private pollTimer: ReturnType<typeof setInterval> | null = null;
  private pollInFlight = false;
  private copiedTimer: ReturnType<typeof setTimeout> | null = null;
  /** The item completion timestamps last seen, joined; a change means questions moved on the server. */
  private lastCompletionKey = '';
  private diagnosticsAutoOpened = false;
  /** Question id to item revision, as of the last load. */
  private knownRevisions = new Map<number, number>();
  private questionsLoadedOnce = false;
  private questionsRequestSeq = 0;

  ngOnInit(): void {
    // The row and card actions carry interestfor + popover="hint" tooltips.
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible'] || changes['suite']) {
      if (this.visible && this.suite) {
        const suiteChanged = !!changes['suite']
          && changes['suite'].previousValue?.id !== changes['suite'].currentValue?.id;
        if (!this.isOpen) {
          this.openDialog();
        } else if (suiteChanged) {
          this.resetForSuite();
        }
      } else if (this.isOpen) {
        // The host lowered `visible`; close without echoing a close request back.
        this.closeDialog();
      }
    }

    if (changes['benchmarkCapableConfigs'] && this.isOpen && !this.selectedModel) {
      this.applyDefaultModel();
      this.cdr.markForCheck();
    }
  }

  ngOnDestroy(): void {
    this.stopPolling();
    if (this.copiedTimer) {
      clearTimeout(this.copiedTimer);
      this.copiedTimer = null;
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    if (this.isModelDropdownOpen && !target?.closest('.qg-model-selector')) {
      this.isModelDropdownOpen = false;
      this.cdr.markForCheck();
    }
  }

  // -------------------------------------------------------------------------------------------
  // Dialog lifecycle
  // -------------------------------------------------------------------------------------------

  private openDialog(): void {
    this.isOpen = true;
    this.resetForSuite();
    this.dialog?.nativeElement.showModal();
    this.cdr.detectChanges();
    this.heading?.nativeElement.focus();
  }

  /** Clears everything tied to a suite or a job. Counts and instructions carry over between opens. */
  private resetForSuite(): void {
    this.stopPolling();
    this.job = null;
    this.jobId = null;
    this.jobStarting = false;
    this.retrying = false;
    this.cancelling = false;
    this.dialogError = null;
    this.lastPollAtUtc = null;
    this.lastPollError = null;
    this.questions = [];
    this.selectedIds = new Set<number>();
    this.newIds = new Set<number>();
    this.anyQuestionsChanged = false;
    this.diagnosticsOpen = false;
    this.diagnosticsAutoOpened = false;
    this.copiedDiagnostics = false;
    this.diagnosticsCopyFailed = false;
    this.isModelDropdownOpen = false;
    this.lastCompletionKey = '';
    this.knownRevisions.clear();
    this.questionsLoadedOnce = false;
    this.applyDefaultModel();

    this.loadQuestions(false);
    this.adoptActiveJob();
    this.cdr.markForCheck();
  }

  private applyDefaultModel(): void {
    const configs = this.benchmarkCapableConfigs ?? [];
    if (this.defaultModelConfigId != null && configs.some(c => c.id === this.defaultModelConfigId)) {
      this.modelConfigId = this.defaultModelConfigId;
    } else {
      this.modelConfigId = configs[0]?.id ?? null;
    }
  }

  /** A job already running for this suite, started earlier and sent to the background, is resumed. */
  private adoptActiveJob(): void {
    const suiteId = this.suite?.id;
    if (suiteId == null) return;
    this.benchmarkService.getActiveQuestionGeneration().subscribe({
      next: (active) => {
        if (!this.isOpen || this.suite?.id !== suiteId || this.jobId) return;
        if (active && active.suiteId === suiteId && active.status === 'Running') {
          this.adoptJob(active.id, active);
        }
      },
      // Nothing to resume is the common case; a failed lookup leaves the setup usable.
      error: (err) => console.warn('Failed to look up an active question generation job', err)
    });
  }

  /** Closes without telling the host; used when the host itself lowered `visible`. */
  private closeDialog(): void {
    this.isOpen = false;
    this.stopPolling();
    this.isModelDropdownOpen = false;
    if (this.confirmDialog?.nativeElement.open) {
      this.confirmDialog.nativeElement.close();
    }
    this.confirmAction = null;
    this.dialog?.nativeElement.close();
    this.cdr.markForCheck();
  }

  /** Escape, the close button, *Close* and *Run in Background* all land here. */
  close(): void {
    if (!this.isOpen) return;
    const questionsChanged = this.anyQuestionsChanged;
    this.closeDialog();
    this.closed.emit({ questionsChanged });
  }

  // -------------------------------------------------------------------------------------------
  // Model selector
  // -------------------------------------------------------------------------------------------

  toggleModelDropdown(event: Event): void {
    event.stopPropagation();
    if (this.setupLocked) return;
    this.isModelDropdownOpen = !this.isModelDropdownOpen;
    this.cdr.markForCheck();
  }

  selectModel(config: SystemAiConfigDto, event?: Event): void {
    event?.preventDefault();
    this.modelConfigId = config.id;
    this.isModelDropdownOpen = false;
    this.cdr.markForCheck();
  }

  get selectedModel(): SystemAiConfigDto | undefined {
    return (this.benchmarkCapableConfigs ?? []).find(c => c.id === this.modelConfigId);
  }

  get totalCount(): number {
    return (this.simpleCount || 0) + (this.intermediateCount || 0) + (this.advancedCount || 0);
  }

  // -------------------------------------------------------------------------------------------
  // Derived job state
  // -------------------------------------------------------------------------------------------

  get isRunning(): boolean {
    return this.job?.status === 'Running';
  }

  get isTerminal(): boolean {
    return !!this.job && this.job.status !== 'Running';
  }

  get setupLocked(): boolean {
    return this.isRunning || this.jobStarting || this.retrying;
  }

  get dialogTitle(): string {
    switch (this.job?.status) {
      case undefined: return 'Generate Benchmark Questions';
      case 'Running': return 'Generating Benchmark Questions';
      case 'Completed': return 'Question Generation Complete';
      case 'CompletedWithErrors': return 'Question Generation Completed with Errors';
      case 'Failed': return 'Question Generation Failed';
      case 'Cancelled': return 'Question Generation Cancelled';
      default: return 'Generate Benchmark Questions';
    }
  }

  get startLabel(): string {
    return this.job ? 'Generate More' : 'Start Generation';
  }

  /** Items the job actually asked for; a band requested at zero is skipped and not shown. */
  get visibleItems(): QuestionGenerationJobItemDto[] {
    return (this.job?.items ?? []).filter(i => i.status !== 'Skipped');
  }

  get requestedTotal(): number {
    return this.visibleItems.reduce((sum, i) => sum + (i.requestedCount || 0), 0);
  }

  get generatedTotal(): number {
    return this.visibleItems.reduce((sum, i) => sum + (i.generatedCount || 0), 0);
  }

  get itemsDone(): number {
    return this.visibleItems.filter(i => {
      const state = this.itemState(i);
      return state === 'completed' || state === 'partial' || state === 'failed';
    }).length;
  }

  get retryableBands(): QuestionGenerationJobItemDto[] {
    return this.visibleItems.filter(i => {
      if (i.kind !== 'Band') return false;
      const state = this.itemState(i);
      return state === 'failed' || state === 'partial';
    });
  }

  bandLabel(difficulty: number | string): string {
    return formatDifficulty(difficulty);
  }

  itemLabel(item: QuestionGenerationJobItemDto): string {
    switch (item.kind) {
      case 'RubricOnly': return `Q${item.targetQuestionOrderIndex ?? '?'} rubric`;
      case 'ReplaceQuestion': return `Q${item.targetQuestionOrderIndex ?? '?'} question`;
      default: return this.bandLabel(item.difficulty);
    }
  }

  /** The count for a band; the target question's excerpt for a per-question item. */
  itemDetail(item: QuestionGenerationJobItemDto): string {
    if (item.kind === 'Band' || !item.kind) {
      return `${item.generatedCount} / ${item.requestedCount}`;
    }
    return item.targetQuestionExcerpt ?? '';
  }

  itemState(item: QuestionGenerationJobItemDto): QuestionGenerationItemState {
    switch (item.status) {
      case 'Generating': return 'generating';
      case 'Repairing': return 'repairing';
      case 'Completed': return item.generatedCount < item.requestedCount ? 'partial' : 'completed';
      case 'Failed': return 'failed';
      case 'Skipped': return 'skipped';
      default: return 'pending';
    }
  }

  itemStateLabel(item: QuestionGenerationJobItemDto): string {
    const state = this.itemState(item);
    return state.charAt(0).toUpperCase() + state.slice(1);
  }

  /** Band retry controls exist only once the job has stopped, and only for band items. */
  canActOnBand(item: QuestionGenerationJobItemDto): boolean {
    return this.isTerminal && item.kind === 'Band';
  }

  get progressLabel(): string {
    const job = this.job;
    if (!job) {
      return this.jobStarting ? 'Starting question generation…' : '';
    }

    const items = this.visibleItems;
    const generated = this.generatedTotal;
    const requested = this.requestedTotal;

    if (job.status === 'Running') {
      const active = items.find(i => i.status === 'Generating' || i.status === 'Repairing')
        ?? items.find(i => i.status === 'Pending');
      if (!active) {
        return 'Finishing question generation…';
      }
      const position = items.indexOf(active) + 1;
      if (active.status === 'Repairing') {
        return `Repairing ${this.itemLabel(active)} response…`;
      }
      switch (active.kind) {
        case 'RubricOnly':
          return `Regenerating rubric for Q${active.targetQuestionOrderIndex ?? '?'} (${position} of ${items.length})…`;
        case 'ReplaceQuestion':
          return `Regenerating Q${active.targetQuestionOrderIndex ?? '?'} and its rubric (${position} of ${items.length})…`;
        default:
          return `Generating ${this.bandLabel(active.difficulty)} questions (item ${position} of ${items.length})…`;
      }
    }

    const verb = job.jobKind === 'Regeneration' ? 'Regenerated' : 'Generated';
    switch (job.status) {
      case 'Failed':
        return `Failed: ${this.firstErrorMessage ?? 'see the diagnostics for details'}`;
      case 'Cancelled':
        return `Cancelled after ${generated} of ${requested}`;
      case 'CompletedWithErrors': {
        const failed = items.filter(i => this.itemState(i) === 'failed').length;
        return `${verb} ${generated} of ${requested}; ${failed} item(s) failed`;
      }
      default:
        return `${verb} ${generated} of ${requested}`;
    }
  }

  private get firstErrorMessage(): string | null {
    const logError = this.job?.log?.find(l => (l.severity || '').toLowerCase() === 'error');
    if (logError) return logError.message;
    return this.job?.items?.find(i => i.errorMessage)?.errorMessage ?? null;
  }

  /** The alert text for a job that ended badly; null for any other state. */
  get jobFailureText(): string | null {
    const job = this.job;
    if (!job) return null;
    if (job.status === 'Failed') {
      return `Question generation failed: ${this.firstErrorMessage ?? 'see the diagnostics for details.'}`;
    }
    if (job.status === 'CompletedWithErrors') {
      const failed = this.visibleItems.filter(i => this.itemState(i) === 'failed').map(i => this.itemLabel(i));
      return failed.length > 0
        ? `Some items failed: ${failed.join(', ')}. Adjust the setup and retry them.`
        : 'Question generation completed with errors; see the diagnostics.';
    }
    return null;
  }

  get elapsedLabel(): string {
    if (!this.job?.startedAtUtc) return '—';
    return this.formatElapsed(elapsedMsBetween(this.job.startedAtUtc, this.job.completedAtUtc));
  }

  // -------------------------------------------------------------------------------------------
  // Questions panel state
  // -------------------------------------------------------------------------------------------

  get selectionCount(): number {
    return this.selectedIds.size;
  }

  get newCount(): number {
    return this.newIds.size;
  }

  isSelected(id: number): boolean {
    return this.selectedIds.has(id);
  }

  toggleSelection(id: number): void {
    const next = new Set(this.selectedIds);
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this.selectedIds = next;
    this.cdr.markForCheck();
  }

  clearSelection(): void {
    this.selectedIds = new Set<number>();
    this.cdr.markForCheck();
  }

  /** Reloads the question list; used by the host after its question form saves. */
  refreshQuestions(): void {
    this.loadQuestions(false);
  }

  /**
   * Loads the suite's questions. With `markNew`, questions that did not exist before or whose
   * revision rose are added to `newIds`.
   */
  private loadQuestions(markNew: boolean): void {
    const suiteId = this.suite?.id;
    if (suiteId == null) return;
    const seq = ++this.questionsRequestSeq;
    this.questionsLoading = true;
    this.cdr.markForCheck();

    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (questions) => {
        if (seq !== this.questionsRequestSeq || this.suite?.id !== suiteId) return;
        const sorted = [...(questions ?? [])].sort((a, b) => a.orderIndex - b.orderIndex);

        if (markNew && this.questionsLoadedOnce) {
          const next = new Set(this.newIds);
          for (const q of sorted) {
            const previous = this.knownRevisions.get(q.id);
            if (previous === undefined || (q.itemRevision ?? 0) > previous) {
              next.add(q.id);
            }
          }
          this.newIds = next;
        }

        this.knownRevisions = new Map(sorted.map(q => [q.id, q.itemRevision ?? 0] as [number, number]));
        this.questionsLoadedOnce = true;

        const ids = new Set(sorted.map(q => q.id));
        this.selectedIds = new Set([...this.selectedIds].filter(id => ids.has(id)));
        this.newIds = new Set([...this.newIds].filter(id => ids.has(id)));

        this.questions = sorted;
        this.questionsLoading = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (seq !== this.questionsRequestSeq) return;
        this.questionsLoading = false;
        this.dialogError = this.describeError(err, 'Failed to load the suite questions.');
        this.cdr.markForCheck();
      }
    });
  }

  // -------------------------------------------------------------------------------------------
  // Starting and following a job
  // -------------------------------------------------------------------------------------------

  start(): void {
    const suite = this.suite;
    if (!suite || this.modelConfigId == null || this.setupLocked) return;
    if (this.totalCount <= 0) {
      this.dialogError = 'Request at least one question.';
      this.cdr.markForCheck();
      return;
    }
    this.jobStarting = true;
    this.dialogError = null;
    this.cdr.markForCheck();

    this.benchmarkService.startQuestionGeneration({
      suiteId: suite.id,
      generatorModelConfigurationId: this.modelConfigId,
      simpleCount: this.simpleCount || 0,
      intermediateCount: this.intermediateCount || 0,
      advancedCount: this.advancedCount || 0,
      instructions: this.instructions.trim() || undefined
    }).subscribe({
      next: (res) => {
        this.jobStarting = false;
        this.adoptJob(res.jobId);
      },
      error: (err) => {
        this.jobStarting = false;
        this.handleStartError(err, 'Failed to start question generation.');
      }
    });
  }

  /** Retries the given bands of the current, finished job with the setup column's model and instructions. */
  retryBands(difficulties: number[], discardExisting: boolean): void {
    const job = this.job;
    if (!job || !this.isTerminal || this.setupLocked || difficulties.length === 0) return;
    this.retrying = true;
    this.dialogError = null;
    this.cdr.markForCheck();

    this.benchmarkService.retryQuestionGeneration(job.id, {
      difficulties,
      discardExisting,
      generatorModelConfigurationId: this.modelConfigId,
      instructions: this.instructions.trim()
    }).subscribe({
      next: (res) => {
        this.retrying = false;
        this.adoptJob(res.jobId);
      },
      error: (err) => {
        this.retrying = false;
        this.handleStartError(err, 'Failed to retry question generation.');
      }
    });
  }

  retryFailedBands(): void {
    this.retryBands(this.retryableBands.map(i => i.difficulty), false);
  }

  retryBand(item: QuestionGenerationJobItemDto): void {
    this.retryBands([item.difficulty], false);
  }

  discardAndRegenerateBand(item: QuestionGenerationJobItemDto): void {
    if (!this.canActOnBand(item) || this.setupLocked) return;
    const band = this.bandLabel(item.difficulty);
    const count = item.createdQuestionCount;
    this.openConfirm({
      title: `Discard and Regenerate ${band} Questions`,
      message: `The ${count} ${band} question(s) this job created will be deleted and ${item.requestedCount} new ones generated with the current setup.`,
      dangerNotice: 'Questions you have edited or verified since they were generated are deleted too.',
      buttonText: 'Discard and Regenerate',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => this.retryBands([item.difficulty], true)
    });
  }

  /** `Question` scope confirms first; `Rubric` scope runs directly. */
  regenerate(ids: number[], scope: RegenerateQuestionsScope): void {
    if (ids.length === 0 || this.setupLocked) return;
    if (scope === 'Question') {
      this.openConfirm({
        title: ids.length === 1 ? 'Regenerate Question' : 'Regenerate Questions',
        message: `The text and rubric of ${ids.length} question(s) will be replaced in place; their revision is bumped and any review is cleared.`,
        dangerNotice: '',
        buttonText: ids.length === 1 ? 'Regenerate Question' : 'Regenerate Questions',
        buttonClass: 'btn-gh btn-gh-delete',
        action: () => this.runRegeneration(ids, scope)
      });
      return;
    }
    this.runRegeneration(ids, scope);
  }

  regenerateSelected(scope: RegenerateQuestionsScope): void {
    this.regenerate([...this.selectedIds], scope);
  }

  private runRegeneration(ids: number[], scope: RegenerateQuestionsScope): void {
    const suite = this.suite;
    if (!suite || this.setupLocked) return;
    if (this.modelConfigId == null) {
      this.dialogError = 'Select a generator model first.';
      this.cdr.markForCheck();
      return;
    }
    this.jobStarting = true;
    this.dialogError = null;
    this.cdr.markForCheck();

    this.benchmarkService.regenerateQuestions({
      suiteId: suite.id,
      questionIds: ids,
      scope,
      generatorModelConfigurationId: this.modelConfigId,
      instructions: this.instructions.trim()
    }).subscribe({
      next: (res) => {
        this.jobStarting = false;
        this.selectedIds = new Set<number>();
        this.adoptJob(res.jobId);
      },
      error: (err) => {
        this.jobStarting = false;
        this.handleStartError(err, 'Failed to start question regeneration.');
      }
    });
  }

  /** A 409 carries the job already running, which is followed instead. */
  private handleStartError(err: any, fallback: string): void {
    if (err?.status === 409 && err.error?.id) {
      const running = err.error as QuestionGenerationJobDto;
      this.adoptJob(running.id, running);
      return;
    }
    this.dialogError = this.describeError(err, fallback);
    this.cdr.markForCheck();
  }

  /** Follows a job: polls it until it stops. `initial` is a job DTO already in hand. */
  private adoptJob(jobId: string, initial?: QuestionGenerationJobDto): void {
    this.jobId = jobId;
    this.diagnosticsOpen = false;
    this.diagnosticsAutoOpened = false;
    this.newIds = new Set<number>();
    this.lastPollError = null;
    this.lastCompletionKey = initial ? this.completionKeyOf(initial) : '';
    if (initial) {
      this.job = initial;
    }
    this.startPolling(jobId);
    this.cdr.markForCheck();
    this.heading?.nativeElement.focus();
  }

  private startPolling(jobId: string): void {
    this.stopPolling();
    // The interval is set before the first poll, so a first poll that already finds the job
    // stopped clears it.
    this.pollTimer = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) return;
      this.poll(jobId);
    }, QuestionGenerationDialogComponent.POLL_INTERVAL_MS);
    this.poll(jobId);
  }

  private stopPolling(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
    this.pollInFlight = false;
  }

  private completionKeyOf(job: QuestionGenerationJobDto): string {
    return (job.items ?? [])
      .map(i => i.completedAtUtc ?? '')
      .filter(v => v !== '')
      .join('|');
  }

  private poll(jobId: string): void {
    if (this.pollInFlight) return;
    this.pollInFlight = true;
    this.benchmarkService.getQuestionGeneration(jobId).subscribe({
      next: (job) => {
        this.pollInFlight = false;
        if (this.jobId !== jobId || !this.isOpen) return;
        this.job = job;
        this.lastPollAtUtc = new Date().toISOString();
        this.lastPollError = null;

        const key = this.completionKeyOf(job);
        if (key !== this.lastCompletionKey) {
          this.lastCompletionKey = key;
          if (key !== '') {
            this.anyQuestionsChanged = true;
            this.questionsChanged.emit(job.suiteId);
            this.loadQuestions(true);
          }
        }

        if ((job.status === 'Failed' || job.status === 'CompletedWithErrors') && !this.diagnosticsAutoOpened) {
          this.diagnosticsAutoOpened = true;
          this.diagnosticsOpen = true;
        }

        if (job.status !== 'Running') {
          this.stopPolling();
          this.cancelling = false;
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.pollInFlight = false;
        if (this.jobId !== jobId) return;
        this.lastPollAtUtc = new Date().toISOString();
        this.lastPollError = this.describeError(err, 'Polling failed');
        this.dialogError = `Lost contact with the generation job: ${this.lastPollError}`;
        this.stopPolling();
        this.cdr.markForCheck();
      }
    });
  }

  cancel(): void {
    const job = this.job;
    if (!job || !this.isRunning || this.cancelling) return;
    this.cancelling = true;
    this.cdr.markForCheck();
    this.benchmarkService.cancelQuestionGeneration(job.id).subscribe({
      next: () => {
        this.poll(job.id);
      },
      error: (err) => {
        this.cancelling = false;
        this.dialogError = this.describeError(err, 'Failed to cancel question generation.');
        this.cdr.markForCheck();
      }
    });
  }

  onDiagnosticsToggle(event: Event): void {
    this.diagnosticsOpen = (event.target as HTMLDetailsElement).open;
  }

  // -------------------------------------------------------------------------------------------
  // Card actions
  // -------------------------------------------------------------------------------------------

  toggleReview(question: BenchmarkQuestionDto): void {
    this.benchmarkService.reviewQuestion(question.id, !question.isReviewed).subscribe({
      next: (updated) => {
        question.isReviewed = updated.isReviewed;
        question.reviewedAtRevision = updated.reviewedAtRevision;
        question.reviewedAtUtc = updated.reviewedAtUtc;
        question.reviewedByUserId = updated.reviewedByUserId;
        this.anyQuestionsChanged = true;
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.dialogError = this.describeError(err, 'Failed to update the question review.');
        this.cdr.markForCheck();
      }
    });
  }

  edit(question: BenchmarkQuestionDto): void {
    if (this.setupLocked) return;
    this.editQuestionRequested.emit(question);
  }

  // -------------------------------------------------------------------------------------------
  // Local confirmation dialog
  // -------------------------------------------------------------------------------------------

  private openConfirm(options: {
    title: string;
    message: string;
    dangerNotice: string;
    buttonText: string;
    buttonClass: string;
    action: () => void;
  }): void {
    this.confirmTitle = options.title;
    this.confirmMessage = options.message;
    this.confirmDangerNotice = options.dangerNotice;
    this.confirmButtonText = options.buttonText;
    this.confirmButtonClass = options.buttonClass;
    this.confirmAction = options.action;
    this.cdr.detectChanges();
    this.confirmDialog?.nativeElement.showModal();
  }

  closeConfirm(): void {
    this.confirmAction = null;
    if (this.confirmDialog?.nativeElement.open) {
      this.confirmDialog.nativeElement.close();
    }
    this.cdr.markForCheck();
  }

  executeConfirm(): void {
    const action = this.confirmAction;
    this.closeConfirm();
    action?.();
  }

  // -------------------------------------------------------------------------------------------
  // Diagnostics. Ids, counts, statuses, the operator instructions and truncated provider output;
  // never an API key. Operators paste these into bug reports.
  // -------------------------------------------------------------------------------------------

  get diagnosticsText(): string {
    const lines: string[] = [];
    lines.push('=== QUESTION GENERATION DIAGNOSTICS ===');
    lines.push(`Captured:         ${new Date().toISOString()}`);
    lines.push(`Overseer build:   ${this.overseerBuildVersion || 'unknown'}`);
    lines.push(`Client:           ${typeof navigator !== 'undefined' ? navigator.userAgent : 'unknown'}`);
    let tz = 'unknown';
    let offsetStr = '';
    try {
      tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
      const offsetMin = -new Date().getTimezoneOffset();
      const sign = offsetMin >= 0 ? '+' : '-';
      const absMin = Math.abs(offsetMin);
      const h = String(Math.floor(absMin / 60)).padStart(2, '0');
      const m = String(absMin % 60).padStart(2, '0');
      offsetStr = ` (UTC${sign}${h}:${m})`;
    } catch {
      // A locale without a resolvable zone is not worth failing a capture over.
    }
    lines.push(`Client timezone:  ${tz}${offsetStr}`);
    lines.push(`Page:             ${typeof location !== 'undefined' ? location.pathname : ''}`);
    lines.push('');

    const job = this.job;
    if (!job) {
      lines.push('No job received yet.');
      lines.push(`Suite: ${this.suite?.name ?? 'none'} (${this.suite?.id ?? 'n/a'})`);
      if (this.dialogError) lines.push(`Dialog error: ${this.dialogError}`);
      if (this.lastPollError) lines.push(`Last poll error: ${this.lastPollError}`);
      lines.push('');
      return lines.join('\n');
    }

    lines.push('--- JOB ---');
    lines.push(`Job ID: ${job.id}, Kind: ${job.jobKind || 'Generation'}, Status: ${job.status}`);
    if (job.retryOfJobId) lines.push(`Retry of job: ${job.retryOfJobId}`);
    lines.push(`Suite: ${job.suiteName} (${job.suiteId})`);
    lines.push(`Snapshot: ${job.gameSnapshotName ?? this.suite?.gameSnapshotName ?? 'n/a'} (${job.gameSnapshotId ?? this.suite?.gameSnapshotId ?? 'n/a'})`);
    lines.push(`Started (raw):    ${job.startedAtUtc}`);
    lines.push(`Completed (raw):  ${job.completedAtUtc ?? 'n/a'}`);
    lines.push(`Elapsed:          ${this.elapsedLabel}`);
    lines.push(`Progress:         ${this.progressLabel}`);
    lines.push('');

    lines.push('--- GENERATOR ---');
    lines.push(`Config: ${job.generatorDisplayName} (id ${job.generatorConfigId})`);
    lines.push(`Provider: ${job.generatorProvider ?? 'n/a'}, model: ${job.generatorModelId ?? 'n/a'}`);
    lines.push(`Thinking: ${job.generatorThinkingLevel ?? 'default'}, reasoning: ${job.generatorReasoningMode ?? 'default'}, service tier: ${formatServiceTier(job.generatorServiceTier)}`);
    const selected = this.selectedModel;
    lines.push(`Setup column now: ${selected ? `${selected.displayName || selected.modelId} (id ${selected.id})` : 'none selected'}`);
    lines.push('');

    lines.push('--- INSTRUCTIONS ---');
    lines.push(job.instructions ? job.instructions : '(none)');
    lines.push('');

    lines.push('--- ITEMS ---');
    const items = job.items ?? [];
    if (items.length === 0) {
      lines.push('none');
    }
    for (const item of items) {
      const duration = item.startedAtUtc
        ? this.formatElapsed(elapsedMsBetween(item.startedAtUtc, item.completedAtUtc))
        : 'n/a';
      lines.push(
        `${this.itemLabel(item)} [${item.kind || 'Band'}, ${item.difficultyName || this.bandLabel(item.difficulty)}]`
        + (item.targetQuestionId != null ? ` target question id ${item.targetQuestionId}` : '')
        + `: requested ${item.requestedCount}, generated ${item.generatedCount}, `
        + `created ${item.createdQuestionCount ?? 0}, updated ${item.updatedQuestionCount ?? 0}, discarded ${item.discardedQuestionCount ?? 0}, `
        + `status ${item.status}, calls ${item.modelCalls ?? 0}, tokens ${item.promptTokens ?? 0} prompt / ${item.outputTokens ?? 0} output, `
        + `started ${item.startedAtUtc ?? 'n/a'}, completed ${item.completedAtUtc ?? 'n/a'}, duration ${duration}`
        + (item.errorMessage ? `, error: ${item.errorMessage}` : ''));
    }
    lines.push('');

    lines.push('--- USAGE ---');
    lines.push(`Model calls: ${job.totalModelCalls ?? 0}, prompt tokens: ${job.promptTokens}, output tokens: ${job.outputTokens}`);
    lines.push('');

    lines.push('--- LOG ---');
    const log = job.log ?? [];
    if (log.length === 0) {
      lines.push('empty');
    }
    for (const entry of log) {
      lines.push(`[${entry.timestampUtc}] ${(entry.severity || 'info').toUpperCase()} ${entry.message}`);
      if (entry.rawExcerpt) {
        lines.push(`  Excerpt: ${entry.rawExcerpt.replace(/\r?\n/g, '\n           ')}`);
      }
    }
    lines.push('');

    lines.push('--- POLLING ---');
    lines.push(`Job poll: ${this.pollTimer ? `active every ${QuestionGenerationDialogComponent.POLL_INTERVAL_MS} ms` : 'stopped'}`);
    if (this.lastPollAtUtc) lines.push(`Last poll: ${this.lastPollAtUtc}`);
    if (this.lastPollError) lines.push(`Last poll error: ${this.lastPollError}`);
    if (this.dialogError) lines.push(`Dialog error: ${this.dialogError}`);
    lines.push('');

    return lines.join('\n');
  }

  get diagnosticsCopyStatus(): string {
    if (this.copiedDiagnostics) return 'Question generation diagnostics copied to clipboard';
    return this.diagnosticsCopyFailed ? 'Could not copy the question generation diagnostics to the clipboard.' : '';
  }

  async copyDiagnostics(): Promise<void> {
    const copied = await copyToClipboard(this.diagnosticsText);
    if (this.copiedTimer) {
      clearTimeout(this.copiedTimer);
      this.copiedTimer = null;
    }
    if (copied) {
      this.diagnosticsCopyFailed = false;
      this.copiedDiagnostics = true;
      this.copiedTimer = setTimeout(() => {
        this.copiedDiagnostics = false;
        this.copiedTimer = null;
        this.cdr.markForCheck();
      }, QuestionGenerationDialogComponent.COPIED_RESET_MS);
    } else {
      this.copiedDiagnostics = false;
      this.diagnosticsCopyFailed = true;
      this.dialogError = 'Could not copy the question generation diagnostics to the clipboard.';
    }
    this.cdr.markForCheck();
  }

  // -------------------------------------------------------------------------------------------
  // Formatting
  // -------------------------------------------------------------------------------------------

  formatElapsed(ms: number): string {
    if (!ms || ms < 0) return '0s';
    const totalSecs = Math.floor(ms / 1000);
    const hours = Math.floor(totalSecs / 3600);
    const mins = Math.floor((totalSecs % 3600) / 60);
    const secs = totalSecs % 60;
    const pad = (n: number) => (n < 10 ? '0' + n : '' + n);
    if (hours > 0) return `${hours}h ${pad(mins)}m ${pad(secs)}s`;
    if (mins > 0) return `${mins}m ${pad(secs)}s`;
    return `${secs}s`;
  }

  severityClass(severity: string | null | undefined): string {
    return 'log-entry severity-' + (severity || 'info').toLowerCase();
  }

  private describeError(err: any, fallback: string): string {
    const httpStatus = err?.status ? ` (HTTP ${err.status})` : '';
    const body = err?.error;
    const msg = typeof body === 'string' && body
      ? body
      : (body?.error || body?.message || err?.message || fallback);
    return `${msg}${httpStatus}`;
  }
}
