import { Component, ChangeDetectorRef, ElementRef, EventEmitter, OnDestroy, Output, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkQuestionDto,
  CreateBenchmarkQuestionRequest
} from '../../../services/admin-benchmark.service';
import { CollapsibleMarkdownComponent } from '../../../shared/collapsible-markdown/collapsible-markdown.component';
import { MarkdownEditorComponent } from '../../../shared/markdown-editor/markdown-editor.component';
import { refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { formatThinkingLevel, showReasoningBadge, formatServiceTier, formatDifficulty } from '../../../utils/model-badge-format.util';
import { firstValueFrom, map } from 'rxjs';
import {
  questionYamlFileName,
  serializeQuestionsYaml
} from '../question-yaml/question-yaml-format';
import { copyTextFromPromise, copyToClipboard } from '../../../utils/clipboard.util';
import { downloadTextFile } from '../../../utils/download.util';
import { ImportMode } from '../question-yaml/question-yaml-format';
import { COPY_STATUS_MS, SNAPSHOT_TEXT_EXPORT_FAILED } from '../benchmark.models';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** Manage Questions and the question form, opened from a suite card. */
@Component({
  selector: 'app-suite-questions-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule, CollapsibleMarkdownComponent, MarkdownEditorComponent
  ],
  templateUrl: './suite-questions-dialog.component.html',
  styleUrls: ['./suite-questions-dialog.component.scss']
})
export class SuiteQuestionsDialogComponent implements OnDestroy {
  private readonly viewSync = inject(BenchmarkViewSync);
  readonly bridge = inject(BenchmarkShellBridge);
  readonly workspace = inject(BenchmarkWorkspaceStore);
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);
  formatDifficulty(diff: string | number): string {
    return formatDifficulty(diff);
  }
  formatThinkingLevel(level: string | null | undefined): string {
    return formatThinkingLevel(level);
  }
  formatServiceTier(tier: string | null | undefined): string {
    return formatServiceTier(tier);
  }
  showReasoningBadge(mode: string | null | undefined): boolean {
    return showReasoningBadge(mode);
  }

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
    });
    this.workspace.questionsLoaded$.pipe(takeUntilDestroyed()).subscribe(loaded => {
      const pendingId = this.pendingQuestionEditId;
      this.pendingQuestionEditId = null;
      const question = loaded && pendingId != null ? this.workspace.questions.find(q => q.id === pendingId) : undefined;
      if (question) {
        this.openEditQuestion(question);
      }
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.questionsCopyStatusTimer);
  }

  /** A question was saved into this suite; the host reloads the suite cards and the generation dialog. */
  @Output() questionSaved = new EventEmitter<number>();

  /** Asks the host to open Suite Health on its board-facts tab for this suite. */
  @Output() rubricCheckRequested = new EventEmitter<BenchmarkSuiteDto>();

  /** Asks the host to open the YAML import, which also serves Manage Suites. */
  @Output() yamlImportRequested = new EventEmitter<{ mode: ImportMode; question?: BenchmarkQuestionDto }>();

  /** Asks the host to open the question YAML help. */
  @Output() yamlHelpRequested = new EventEmitter<void>();

  @ViewChild('questionsDialog') questionsDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('questionFormDialog') questionFormDialog!: ElementRef<HTMLDialogElement>;

  /**
   * The criteria editor, for the one thing the host cannot reach through the DOM: putting it
   * back on its Write tab before the dialog is shown again.
   */
  @ViewChild('expectedPointsEditor') expectedPointsEditor?: MarkdownEditorComponent;

  /**
   * Opens Manage Questions for a suite. `pendingQuestionEditId` names a question Suite Health asked
   * to edit; its editor opens once the list has loaded and holds it.
   */
  open(suite: BenchmarkSuiteDto, pendingQuestionEditId: number | null = null): void {
    this.pendingQuestionEditId = pendingQuestionEditId;
    this.workspace.currentSuiteForQuestions = suite;
    this.questionsDialog?.nativeElement.showModal();
    this.workspace.loadQuestions(suite.id);
  }

  openQuestionYamlImport(mode: ImportMode, question?: BenchmarkQuestionDto): void {
    this.yamlImportRequested.emit({ mode, question });
  }

  openQuestionYamlHelp(): void {
    this.yamlHelpRequested.emit();
  }

  /**
   * A question the Suite Health panel asked to edit, opened once the suite's questions have
   * loaded. Cleared on use, so a later manual open of the same list does not reopen the editor.
   */
  pendingQuestionEditId: number | null = null;

  // Question Form Dialog
  editingQuestionId: number | null = null;

  /** The suite the open question form saves into: the edited question's own, or Manage Questions' for a new one. */
  questionFormSuiteId: number | null = null;

  questionForm: CreateBenchmarkQuestionRequest = { questionText: '', difficulty: 1, expectedPoints: '' };

  /** A sample rubric in the shape the assessor reads: the four graded sections and a source line. */
  readonly expectedPointsPlaceholder = [
    '**REQUIRED** (accuracy + completeness)',
    '- Fact 1.',
    '- Fact 2.',
    '',
    '**CRITICAL ERROR** (set criticalError) — when the answer:',
    '- Asserts a major hallucination.',
    '',
    '**SCOPE** (conciseness)',
    '- Out-of-scope details.',
    '',
    '**FORM** (not graded — presentation note only)',
    '- Short table or bullet list.',
    '',
    '**SOURCE** — src/role.c line 1217; GnollHack wiki'
  ].join('\n');

  /** expectedPoints is optional on the request DTO; the editor's value is always a string. */
  get expectedPointsValue(): string {
    return this.questionForm.expectedPoints ?? '';
  }

  set expectedPointsValue(value: string) {
    this.questionForm.expectedPoints = value;
  }

  /** True once the list has finished loading and holds at least one question. */
  get canAutoRateAll(): boolean {
    return !this.workspace.loadingQuestions && this.workspace.questions.length > 0;
  }

  openAutoRateAll(): void {
    if (!this.canAutoRateAll || !this.workspace.currentSuiteForQuestions) return;
    this.bridge.openDifficultyAssessorDialog(this.workspace.currentSuiteForQuestions);
  }

  openCreateQuestion() {
    this.editingQuestionId = null;
    this.questionFormSuiteId = this.workspace.currentSuiteForQuestions?.id ?? null;
    this.questionForm = { questionText: '', difficulty: 1, expectedPoints: '' };
    this.showQuestionForm();
  }

  openEditQuestion(q: BenchmarkQuestionDto) {
    this.editingQuestionId = q.id;
    this.questionFormSuiteId = q.benchmarkSuiteId;
    this.questionForm = {
      questionText: q.questionText,
      difficulty: typeof q.difficulty === 'number' ? q.difficulty : this.parseDifficulty(q.difficulty),
      expectedPoints: q.expectedPoints || ''
    };
    this.showQuestionForm();
  }

  /**
   * Opens the question form dialog on the editor's Write tab, whatever tab the previous session
   * left it on. The anchor-positioning polyfill does not observe DOM mutations, so the editor's
   * toolbar tooltips are re-scanned once the dialog is in the top layer.
   */
  private showQuestionForm(): void {
    this.expectedPointsEditor?.resetToWrite();
    this.questionFormDialog?.nativeElement.showModal();
    refreshAnchorPositioning();
  }

  saveQuestion() {
    const suiteId = this.questionFormSuiteId;
    if (!this.questionForm.questionText.trim() || suiteId == null) return;

    if (this.editingQuestionId) {
      this.benchmarkService.updateQuestion(this.editingQuestionId, this.questionForm).subscribe({
        next: () => this.onQuestionSaved(suiteId),
        error: (err) => console.error('Failed to update question', err)
      });
    } else {
      this.benchmarkService.createQuestion(suiteId, this.questionForm).subscribe({
        next: () => this.onQuestionSaved(suiteId),
        error: (err) => console.error('Failed to create question', err)
      });
    }
  }

  /** Reloads the list when it shows the saved question's suite; Manage Questions may hold another suite. */
  private onQuestionSaved(suiteId: number): void {
    this.questionFormDialog?.nativeElement.close();
    if (this.workspace.currentSuiteForQuestions?.id === suiteId) {
      this.workspace.loadQuestions(suiteId);
    }
    this.questionSaved.emit(suiteId);
  }

  deleteQuestion(id: number) {
    this.bridge.openConfirmDialog({
      title: 'Delete Benchmark Question',
      message: 'Are you sure you want to delete this question?',
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Question',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteQuestion(id).subscribe({
          next: () => {
            if (this.workspace.currentSuiteForQuestions) {
              this.workspace.loadQuestions(this.workspace.currentSuiteForQuestions.id);
              this.workspace.loadSuites();
            }
          },
          error: (err) => console.error('Failed to delete question', err)
        });
      }
    });
  }

  // --- Question Drag & Drop Reordering ---

  onQuestionDragStart(event: DragEvent, index: number) {
    if (event.dataTransfer) {
      event.dataTransfer.setData('text/plain', JSON.stringify({ index }));
      event.dataTransfer.effectAllowed = 'move';
      const target = (event.target as HTMLElement).closest('.question-list-item') as HTMLElement;
      if (target) {
        setTimeout(() => target.classList.add('dragging'), 0);
      }
    }
  }

  onQuestionDragEnd(event: DragEvent) {
    const target = (event.target as HTMLElement).closest('.question-list-item') as HTMLElement;
    if (target) {
      target.classList.remove('dragging');
    }
    const items = document.querySelectorAll('.question-list-item');
    items.forEach(item => item.classList.remove('drag-over', 'drag-over-top', 'drag-over-bottom'));
  }

  onQuestionDragOver(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      const rect = targetItem.getBoundingClientRect();
      const midY = rect.top + rect.height / 2;
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
      if (event.clientY < midY) {
        targetItem.classList.add('drag-over-top');
      } else {
        targetItem.classList.add('drag-over-bottom');
      }
    }
  }

  onQuestionDragLeave(event: DragEvent) {
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
  }

  onQuestionDrop(event: DragEvent, dropIndex: number) {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }

    if (event.dataTransfer && this.workspace.currentSuiteForQuestions) {
      const dataStr = event.dataTransfer.getData('text/plain');
      if (dataStr) {
        try {
          const data = JSON.parse(dataStr);
          const dragIndex = data.index;
          if (dragIndex !== undefined && dragIndex !== dropIndex) {
            const item = this.workspace.questions[dragIndex];
            this.workspace.questions.splice(dragIndex, 1);

            let insertIndex = dropIndex;
            if (targetItem) {
              const rect = targetItem.getBoundingClientRect();
              const midY = rect.top + rect.height / 2;
              if (event.clientY >= midY) {
                insertIndex++;
              }
              if (dragIndex < dropIndex && event.clientY < midY) {
                // Dragging down but dropped on top half
              } else if (dragIndex < dropIndex) {
                insertIndex--;
              }
            }

            this.workspace.questions.splice(insertIndex, 0, item);

            // Re-assign order numbers locally
            this.workspace.questions.forEach((q, idx) => q.orderIndex = idx + 1);

            const orderedIds = this.workspace.questions.map(q => q.id);
            this.benchmarkService.reorderQuestions(this.workspace.currentSuiteForQuestions.id, orderedIds).subscribe({
              next: () => this.workspace.loadQuestions(this.workspace.currentSuiteForQuestions!.id),
              error: (err) => console.error('Failed to reorder questions', err)
            });
          }
        } catch (e) {
          console.error('Failed to parse drag data', e);
        }
      }
    }
  }

  parseDifficulty(diff: string | number): number {
    if (typeof diff === 'number') return diff;
    if (diff === 'Intermediate') return 2;
    if (diff === 'Advanced') return 3;
    return 1;
  }

  // --- YAML import and export ---

  questionsCopyStatus = '';

  questionsCopyStatusTimer: ReturnType<typeof setTimeout> | undefined;

  get canExportQuestions(): boolean {
    return !this.workspace.loadingQuestions && this.workspace.questions.length > 0 && !!this.workspace.currentSuiteForQuestions;
  }

  /** One question when given, otherwise every question of the open suite, with the snapshot text of a snapshot suite. */
  async downloadQuestionYaml(q?: BenchmarkQuestionDto): Promise<void> {
    const suite = this.workspace.currentSuiteForQuestions;
    if (!suite || (!q && !this.canExportQuestions)) return;
    if (q) {
      downloadTextFile(questionYamlFileName(suite.name, q), serializeQuestionsYaml([q], suite));
      return;
    }
    const questions = this.workspace.questions;
    const exported = await firstValueFrom(this.workspace.snapshotForExport(suite));
    downloadTextFile(questionYamlFileName(suite.name), serializeQuestionsYaml(questions, suite, exported.snapshot));
    if (exported.failed) {
      this.setQuestionsCopyStatus(SNAPSHOT_TEXT_EXPORT_FAILED);
    }
  }

  /* For all questions the clipboard write is issued inside the click, before the snapshot text arrives. */
  async copyQuestionYaml(q?: BenchmarkQuestionDto): Promise<void> {
    const suite = this.workspace.currentSuiteForQuestions;
    if (!suite || (!q && !this.canExportQuestions)) return;
    if (q) {
      const copied = await copyToClipboard(serializeQuestionsYaml([q], suite));
      this.setQuestionsCopyStatus(copied ? `Copied question ${q.orderIndex} as YAML.` : `Could not copy; use Download as YAML instead.`);
      return;
    }
    const questions = this.workspace.questions;
    let snapshotFailed = false;
    const text = firstValueFrom(this.workspace.snapshotForExport(suite).pipe(map(exported => {
      snapshotFailed = exported.failed;
      return serializeQuestionsYaml(questions, suite, exported.snapshot);
    })));
    const ok = await copyTextFromPromise(text);
    if (!ok) {
      this.setQuestionsCopyStatus(`Could not copy; use Download as YAML instead.`);
    } else {
      this.setQuestionsCopyStatus(snapshotFailed ? `Copied all questions as YAML. ${SNAPSHOT_TEXT_EXPORT_FAILED}` : `Copied all questions as YAML.`);
    }
  }

  private setQuestionsCopyStatus(message: string): void {
    this.questionsCopyStatus = message;
    this.cdr.detectChanges();
    clearTimeout(this.questionsCopyStatusTimer);
    this.questionsCopyStatusTimer = setTimeout(() => {
      this.questionsCopyStatus = '';
      this.cdr.detectChanges();
    }, COPY_STATUS_MS);
  }

  checkSingleQuestionRubric(suite: BenchmarkSuiteDto, question: BenchmarkQuestionDto): void {
    this.rubricCheckRequested.emit(suite);
  }

  toggleQuestionReview(question: BenchmarkQuestionDto): void {
    const newReviewedState = !question.isReviewed;
    this.benchmarkService.reviewQuestion(question.id, newReviewedState).subscribe({
      next: (updated) => {
        question.isReviewed = updated.isReviewed;
        question.reviewedAtRevision = updated.reviewedAtRevision;
        question.reviewedAtUtc = updated.reviewedAtUtc;
        question.reviewedByUserId = updated.reviewedByUserId;
        if (this.workspace.currentSuiteForQuestions) {
          const genQuestions = this.workspace.questions.filter(q => q.isGenerated);
          this.workspace.currentSuiteForQuestions.reviewedQuestionCount = genQuestions.filter(q => q.isReviewed).length;
        }
        this.cdr.detectChanges();
      }
    });
  }
}
