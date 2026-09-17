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
import { Subscription } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkQuestionDto,
  BenchmarkSuiteDto,
  ImportBenchmarkQuestionsResultDto,
  ImportBenchmarkSuiteSnapshot,
  MatchSnapshotResult
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
  RubricNotice,
  buildImportPlan,
  difficultyNumber,
  lintRubric,
  parseQuestionYaml,
  toImportItems,
  toSuiteSnapshot,
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
  /** Advisory house-format notices for an imported rubric that is present and changed. */
  rubricNotices: RubricNotice[];
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
  /** The snapshot the document carries, in suite mode; null when it carries none. */
  suiteSnapshot: ImportBenchmarkSuiteSnapshot | null = null;
  /** The hash the file claims its board had; a difference from the server's is a warning. */
  fileSha256: string | null = null;
  /** Unticking it imports the suite without the file's snapshot. */
  attachSnapshot = true;
  snapshotCheck: MatchSnapshotResult | null = null;
  snapshotCheckState: 'idle' | 'loading' | 'ready' | 'failed' = 'idle';
  private snapshotCheckSub: Subscription | undefined;
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
    this.cancelSnapshotCheck();
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

    this.suiteSnapshot = this.mode === 'suite' ? toSuiteSnapshot(parsed) : null;
    this.fileSha256 = parsed.suite?.snapshot?.sha256 ?? null;
    this.attachSnapshot = true;
    this.cancelSnapshotCheck();

    const plan = buildImportPlan(parsed, this.mode, this.existing, this.target ?? undefined);
    this.cards = plan.map(item => ({
      rubricNotices: item.rubricChanged && item.parsed.rubric ? lintRubric(item.parsed.rubric, this.suiteHasSnapshot) : [],
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
    this.checkSnapshot();
    this.goToStep(2);
  }

  /** The snapshot the rubric lint grades against: in suite mode, the board about to be attached. */
  get suiteHasSnapshot(): boolean {
    return this.mode === 'suite' ? !!this.suiteSnapshot && this.attachSnapshot : !!this.suite?.gameSnapshotId;
  }

  /** Recomputes the rubric notices, which depend on whether a board will be attached. */
  onAttachSnapshotChange(attach: boolean): void {
    this.attachSnapshot = attach;
    const hasSnapshot = this.suiteHasSnapshot;
    this.cards = this.cards.map(card => ({
      ...card,
      rubricNotices: card.item.rubricChanged && card.item.parsed.rubric ? lintRubric(card.item.parsed.rubric, hasSnapshot) : []
    }));
    this.cdr.detectChanges();
  }

  /** True while the file's own hash disagrees with the hash the server computes from its text. */
  get snapshotHashMismatch(): boolean {
    return !!this.fileSha256 && !!this.snapshotCheck && this.fileSha256 !== this.snapshotCheck.sha256;
  }

  /** What the import will do with the file's snapshot, as the server's preflight reports it. */
  get snapshotOutcome(): string {
    if (this.snapshotCheckState === 'loading') {
      return 'Checking for an identical stored snapshot…';
    }
    if (this.snapshotCheckState === 'failed' || !this.snapshotCheck) {
      return 'Could not check for an identical stored snapshot. The import still attaches one.';
    }

    const name = this.suiteSnapshot?.name || this.resolvedSuiteName;
    const match = this.snapshotCheck.match;
    if (!match) {
      return `The game snapshot ${name} (${this.snapshotCheck.charCount.toLocaleString()} characters) will be created and attached.`;
    }
    if (match.suiteId == null) {
      return `An identical snapshot, ${match.name}, is already stored and belongs to no suite. It will be attached; no copy is made.`;
    }
    return `An identical snapshot, ${match.name}, belongs to suite ${match.suiteName}. `
      + `A snapshot belongs to one suite, so this import stores a copy named ${match.name} (2).`;
  }

  /* One preflight per validated document; it writes nothing, and the import decides again anyway. */
  private checkSnapshot(): void {
    this.cancelSnapshotCheck();
    if (this.mode !== 'suite' || !this.suiteSnapshot) {
      return;
    }
    this.snapshotCheckState = 'loading';
    this.snapshotCheckSub = this.benchmarkService.matchSnapshot(this.suiteSnapshot.text).subscribe({
      next: result => {
        this.snapshotCheck = result;
        this.snapshotCheckState = 'ready';
        this.cdr.detectChanges();
      },
      error: () => {
        this.snapshotCheck = null;
        this.snapshotCheckState = 'failed';
        this.cdr.detectChanges();
      }
    });
  }

  private cancelSnapshotCheck(): void {
    this.snapshotCheckSub?.unsubscribe();
    this.snapshotCheckSub = undefined;
    this.snapshotCheck = null;
    this.snapshotCheckState = 'idle';
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
    this.cancelSnapshotCheck();
    this.goToStep(1);
  }

  apply(): void {
    if (this.applying || this.cards.length === 0) return;
    this.applying = true;
    this.applyError = null;
    this.cdr.detectChanges();

    const items = toImportItems(this.cards.map(c => c.item), this.mode);

    if (this.mode === 'suite') {
      const snapshot = this.attachSnapshot ? this.suiteSnapshot : null;
      this.benchmarkService.importSuite({
        name: this.suiteName,
        description: this.suiteDescription,
        questions: items,
        snapshot
      }).subscribe({
        next: suite => {
          this.applying = false;
          this.doneSummary = `Created suite ${suite.name} with ${suite.questionCount} ${suite.questionCount === 1 ? 'question' : 'questions'}.`
            + this.snapshotDoneSentence(suite.gameSnapshotId ?? null, suite.gameSnapshotName ?? null)
            + ' Run Assess Difficulty before the first benchmark run.';
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

  /** Whether the board on the new suite was reused or created, as the response reports it. */
  private snapshotDoneSentence(snapshotId: number | null, snapshotName: string | null): string {
    if (snapshotId == null) {
      return '';
    }
    const name = snapshotName ?? 'the game snapshot';
    return snapshotId === this.snapshotCheck?.match?.id
      ? ` Attached the existing game snapshot ${name}.`
      : ` Created game snapshot ${name}.`;
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
    this.suiteSnapshot = null;
    this.fileSha256 = null;
    this.attachSnapshot = true;
    this.cancelSnapshotCheck();
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
