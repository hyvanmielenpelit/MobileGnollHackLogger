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
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  ImportBenchmarkQuestionsResultDto
} from '../../../services/admin-benchmark.service';
import { CollapsibleMarkdownComponent } from '../../../shared/collapsible-markdown/collapsible-markdown.component';
import { MarkdownPipe } from '../../../chat/markdown.pipe';
import { formatDifficulty } from '../../../utils/model-badge-format.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { DiffLine, diffLines } from '../../../utils/text-diff.util';
import {
  ImportMode,
  ImportPlanItem,
  ParseIssue,
  buildImportPlan,
  difficultyNumber,
  parseQuestionYaml,
  toImportItems,
  validateForMode
} from './question-yaml-format';

export const MAX_IMPORT_FILE_BYTES = 2 * 1024 * 1024;

export type ImportStep = 1 | 2 | 3;
export type ImportSource = 'paste' | 'file';
export type ReviewView = 'side' | 'diff';

/** A plan item with its diffs computed once, for the review step. */
export interface ReviewCard {
  item: ImportPlanItem;
  questionDiff: DiffLine[];
  rubricDiff: DiffLine[];
}

@Component({
  selector: 'app-question-yaml-import-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, CollapsibleMarkdownComponent, MarkdownPipe],
  templateUrl: './question-yaml-import-dialog.component.html',
  styleUrls: ['./question-yaml-import-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionYamlImportDialogComponent {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('dialog') dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('stepHeading') stepHeading?: ElementRef<HTMLElement>;

  /** The suite whose questions are replaced or extended; unused in suite mode. */
  @Input() suite: BenchmarkSuiteDto | null = null;
  @Input() existing: BenchmarkQuestionDto[] = [];
  /** Every suite, for the name-collision note in suite mode. */
  @Input() suites: BenchmarkSuiteDto[] = [];

  @Output() imported = new EventEmitter<ImportBenchmarkQuestionsResultDto>();
  @Output() suiteImported = new EventEmitter<BenchmarkSuiteDto>();
  @Output() helpRequested = new EventEmitter<void>();

  readonly steps: ReadonlyArray<{ id: ImportStep; label: string }> = [
    { id: 1, label: 'Provide YAML' },
    { id: 2, label: 'Review' },
    { id: 3, label: 'Done' }
  ];
  readonly formatDifficulty = formatDifficulty;

  mode: ImportMode = 'questions';
  target: BenchmarkQuestionDto | null = null;
  step: ImportStep = 1;
  source: ImportSource = 'paste';

  pastedText = '';
  fileText: string | null = null;
  fileName = '';
  fileSize = 0;
  fileError: string | null = null;

  validating = false;
  errors: ParseIssue[] = [];
  notices: string[] = [];
  validSummary: string | null = null;
  /** The text the last passing validation ran on; Review is offered only while it is current. */
  private validatedText: string | null = null;

  cards: ReviewCard[] = [];
  suiteName = '';
  suiteDescription: string | null = null;
  view: ReviewView = 'side';

  applying = false;
  applyError: string | null = null;
  doneSummary = '';

  open(mode: ImportMode, target?: BenchmarkQuestionDto): void {
    this.mode = mode;
    this.target = target ?? null;
    this.reset();
    ensureOverlayPolyfills();
    const el = this.dialog?.nativeElement;
    if (el && !el.open) {
      el.showModal();
    }
    this.cdr.detectChanges();
    setTimeout(() => refreshAnchorPositioning(), 0);
  }

  close(): void {
    if (this.applying) return;
    this.dialog?.nativeElement?.close();
  }

  /* Escape on the dialog; a request in flight is not abandoned half way. */
  onCancel(event: Event): void {
    if (this.applying) {
      event.preventDefault();
    }
  }

  get title(): string {
    switch (this.mode) {
      case 'single':
        return this.target ? `Import YAML into question #${this.target.orderIndex}` : 'Import YAML into a question';
      case 'questions':
        return `Import questions into ${this.suite?.name ?? 'this suite'}`;
      default:
        return 'Import a suite from YAML';
    }
  }

  get currentText(): string {
    return this.source === 'paste' ? this.pastedText : (this.fileText ?? '');
  }

  get canReview(): boolean {
    return !this.validating && this.validatedText !== null && this.validatedText === this.currentText;
  }

  get canValidate(): boolean {
    return !this.validating && this.currentText.trim() !== '';
  }

  get changeCount(): number {
    return this.cards.filter(c => !c.item.unchanged).length;
  }

  get applyLabel(): string {
    if (this.mode === 'single') return 'Replace question';
    if (this.mode === 'suite') return 'Create suite';
    return `Apply ${this.changeCount} ${this.changeCount === 1 ? 'change' : 'changes'}`;
  }

  /** The name the server will give an imported suite, following its "(Imported)" collision rule. */
  get resolvedSuiteName(): string {
    const names = new Set(this.suites.map(s => s.name));
    if (!names.has(this.suiteName)) return this.suiteName;
    let candidate = `${this.suiteName} (Imported)`;
    let counter = 1;
    while (names.has(candidate)) {
      counter++;
      candidate = `${this.suiteName} (Imported ${counter})`;
    }
    return candidate;
  }

  // ---- Step 1 --------------------------------------------------------------------------------

  setSource(source: ImportSource): void {
    this.source = source;
    this.clearValidation();
    this.cdr.detectChanges();
  }

  onPastedTextChange(text: string): void {
    this.pastedText = text;
    this.clearValidation();
  }

  async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    this.clearValidation();
    this.fileError = null;
    if (!file) {
      this.cdr.detectChanges();
      return;
    }
    await this.loadFile(file);
    input.value = '';
  }

  async loadFile(file: File): Promise<void> {
    this.fileError = null;
    if (file.size > MAX_IMPORT_FILE_BYTES) {
      this.fileText = null;
      this.fileName = '';
      this.fileSize = 0;
      this.fileError = `${file.name} is ${formatBytes(file.size)}; the limit is 2 MB.`;
      this.cdr.detectChanges();
      return;
    }
    try {
      this.fileText = await file.text();
      this.fileName = file.name;
      this.fileSize = file.size;
    } catch {
      this.fileText = null;
      this.fileError = `Could not read ${file.name}.`;
    }
    this.clearValidation();
    this.cdr.detectChanges();
  }

  clearFile(): void {
    this.fileText = null;
    this.fileName = '';
    this.fileSize = 0;
    this.fileError = null;
    this.clearValidation();
    this.cdr.detectChanges();
  }

  get fileSizeLabel(): string {
    return formatBytes(this.fileSize);
  }

  async validate(): Promise<boolean> {
    const text = this.currentText;
    if (text.trim() === '') {
      this.errors = [{ line: null, message: this.source === 'paste' ? 'Paste a YAML document first.' : 'Choose a file first.' }];
      this.cdr.detectChanges();
      return false;
    }

    this.validating = true;
    this.cdr.detectChanges();

    const parsed = await parseQuestionYaml(text);
    const modeResult = validateForMode(parsed, this.mode, this.existing, this.target ?? undefined);

    this.validating = false;
    /* The text changed while the parser ran; this verdict belongs to text that is no longer there. */
    if (text !== this.currentText) {
      this.cdr.detectChanges();
      return false;
    }

    this.errors = [...parsed.errors, ...modeResult.errors];
    this.notices = [...parsed.notices, ...modeResult.notices];

    if (this.errors.length > 0) {
      this.validatedText = null;
      this.validSummary = null;
      this.cards = [];
      this.cdr.detectChanges();
      return false;
    }

    const plan = buildImportPlan(parsed, this.mode, this.existing, this.target ?? undefined);
    this.cards = plan.map(item => ({
      item,
      questionDiff: diffLines(item.current?.questionText ?? '', item.parsed.questionText ?? item.current?.questionText ?? '').lines,
      rubricDiff: diffLines(
        item.current?.expectedPoints ?? '',
        item.parsed.rubric === null ? (item.current?.expectedPoints ?? '') : item.parsed.rubric
      ).lines
    }));
    this.suiteName = parsed.suite?.name ?? '';
    this.suiteDescription = parsed.suite?.description ?? null;
    this.validatedText = text;
    this.validSummary = this.describeValid(plan);
    this.cdr.detectChanges();
    return true;
  }

  private describeValid(plan: ImportPlanItem[]): string {
    if (this.mode === 'suite') {
      return `Valid: suite '${this.suiteName}' with ${plan.length} ${plan.length === 1 ? 'question' : 'questions'}.`;
    }
    const replace = plan.filter(p => p.current !== null).length;
    const create = plan.length - replace;
    const parts: string[] = [];
    if (replace > 0 || create === 0) parts.push(`${replace} ${replace === 1 ? 'question' : 'questions'} to replace`);
    if (create > 0) parts.push(`${create} to create`);
    return `Valid: ${parts.join(', ')}.`;
  }

  /* Validates again first, so a pass on text that has since changed cannot slip through. */
  async review(): Promise<void> {
    if (!this.canReview) return;
    if (!(await this.validate())) return;
    this.view = 'side';
    this.goToStep(2);
  }

  requestHelp(): void {
    this.helpRequested.emit();
  }

  // ---- Step 2 --------------------------------------------------------------------------------

  setView(view: ReviewView): void {
    this.view = view;
    this.cdr.detectChanges();
  }

  back(): void {
    if (this.applying) return;
    this.applyError = null;
    this.goToStep(1);
  }

  apply(): void {
    if (this.applying || this.cards.length === 0) return;
    this.applying = true;
    this.applyError = null;
    this.cdr.detectChanges();

    const items = toImportItems(this.cards.map(c => c.item), this.mode);

    if (this.mode === 'suite') {
      this.benchmarkService.importSuite({
        name: this.suiteName,
        description: this.suiteDescription,
        questions: items
      }).subscribe({
        next: suite => {
          this.applying = false;
          this.doneSummary = `Created suite ${suite.name} with ${suite.questionCount} ${suite.questionCount === 1 ? 'question' : 'questions'}.`;
          this.suiteImported.emit(suite);
          this.goToStep(3);
        },
        error: err => this.onApplyError(err)
      });
      return;
    }

    if (!this.suite) {
      this.applying = false;
      this.applyError = 'No suite is open.';
      this.cdr.detectChanges();
      return;
    }

    this.benchmarkService.importQuestions(this.suite.id, { items }).subscribe({
      next: result => {
        this.applying = false;
        this.doneSummary = `Replaced ${result.replacedCount}, created ${result.createdCount}, unchanged ${result.unchangedCount}.`;
        this.imported.emit(result);
        this.goToStep(3);
      },
      error: err => this.onApplyError(err)
    });
  }

  private onApplyError(err: unknown): void {
    this.applying = false;
    this.applyError = errorText(err, 'The import failed.');
    this.cdr.detectChanges();
  }

  // ---- Review helpers ------------------------------------------------------------------------

  cardHeading(card: ReviewCard): string {
    const current = card.item.current;
    return current ? `Replace #${current.orderIndex} (id ${current.id})` : 'Create new question';
  }

  currentDifficultyLabel(card: ReviewCard): string {
    return card.item.current ? formatDifficulty(difficultyNumber(card.item.current.difficulty)) : '';
  }

  importedDifficultyLabel(card: ReviewCard): string {
    const parsed = card.item.parsed.difficulty;
    if (parsed !== null) return formatDifficulty(parsed);
    return card.item.current ? `${this.currentDifficultyLabel(card)} (kept)` : 'Simple (default)';
  }

  diffPrefix(line: DiffLine): string {
    return line.kind === 'added' ? '+ ' : line.kind === 'removed' ? '- ' : '  ';
  }

  // ---- Internals -----------------------------------------------------------------------------

  private goToStep(step: ImportStep): void {
    this.step = step;
    this.cdr.detectChanges();
    this.stepHeading?.nativeElement.focus({ preventScroll: true });
  }

  private clearValidation(): void {
    this.errors = [];
    this.notices = [];
    this.validSummary = null;
    this.validatedText = null;
  }

  private reset(): void {
    this.step = 1;
    this.source = 'paste';
    this.pastedText = '';
    this.fileText = null;
    this.fileName = '';
    this.fileSize = 0;
    this.fileError = null;
    this.validating = false;
    this.clearValidation();
    this.cards = [];
    this.suiteName = '';
    this.suiteDescription = null;
    this.view = 'side';
    this.applying = false;
    this.applyError = null;
    this.doneSummary = '';
  }
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} bytes`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** The server's message from an HttpErrorResponse: a plain string body, `{ error }` or `{ message }`. */
export function errorText(err: unknown, fallback: string): string {
  const body = (err as { error?: unknown })?.error;
  if (typeof body === 'string' && body.trim() !== '') return body;
  if (body && typeof body === 'object') {
    const obj = body as { error?: unknown; message?: unknown; title?: unknown };
    if (typeof obj.error === 'string') return obj.error;
    if (typeof obj.message === 'string') return obj.message;
    if (typeof obj.title === 'string') return obj.title;
  }
  return fallback;
}
