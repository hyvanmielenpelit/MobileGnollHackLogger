import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
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
  BoardFactsCheckDto,
  ImportBenchmarkQuestionsResultDto,
  ImportBenchmarkSuiteSnapshot,
  MatchSnapshotResult
} from '../../../services/admin-benchmark.service';
import { CollapsibleMarkdownComponent } from '../../../shared/collapsible-markdown/collapsible-markdown.component';
import { FilePickerComponent } from '../../../shared/file-picker/file-picker.component';
import { MarkdownPipe } from '../../../chat/markdown.pipe';
import { formatDifficulty } from '../../../utils/model-badge-format.util';
import { DiffLine, diffLines } from '../../../utils/text-diff.util';
import {
  ImportMode,
  ImportPlanItem,
  ParseIssue,
  ParseResult,
  RubricNotice,
  buildImportPlan,
  difficultyNumber,
  isSuiteExportFileName,
  lintRubric,
  parseQuestionYaml,
  toImportItems,
  toSuiteSnapshot,
  validateForMode
} from './question-yaml-format';
import {
  DOWNLOADED_FILE_HINT,
  EMPTY_QUESTIONS_ERROR,
  ExpectationFinding,
  ImportExpectation,
  boardIdentityOf,
  checkImportExpectation,
  resolveImportedSuiteName
} from './import-expectation';
import { PANEL_LABELS } from './suite-workflow-instructions';

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

/** What a host needs to redraw its stepper and footer. */
export interface ImportPanelState {
  step: ImportStep;
  validating: boolean;
  applying: boolean;
  canValidate: boolean;
  canReview: boolean;
  canApply: boolean;
}

/** Whether the server's response did what the route intended, shown on the done step. */
export interface ImportIntentCheck {
  ok: boolean;
  message: string;
}

/**
 * The body of a YAML import: provide the document, review it, apply it. It renders no stepper and
 * no footer; the host owns both and drives the panel through its public methods.
 *
 * With an {@link ImportExpectation} (the Snapshot Suite Wizard) the review step also states the
 * outcome, lists the route checks and requires a confirmation; without one it behaves as the
 * standalone import dialog always has.
 */
@Component({
  selector: 'app-question-yaml-import-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, CollapsibleMarkdownComponent, FilePickerComponent, MarkdownPipe],
  templateUrl: './question-yaml-import-panel.component.html',
  styleUrls: ['./question-yaml-import-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class QuestionYamlImportPanelComponent implements OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('stepHeading') stepHeading?: ElementRef<HTMLElement>;

  @Input() mode: ImportMode = 'questions';
  /** The question a single-question import replaces. */
  @Input() target: BenchmarkQuestionDto | null = null;
  /** The suite whose questions are replaced or extended; unused in suite mode. */
  @Input() suite: BenchmarkSuiteDto | null = null;
  @Input() existing: BenchmarkQuestionDto[] = [];
  /** Every suite, for the name-collision note in suite mode. */
  @Input() suites: BenchmarkSuiteDto[] = [];
  /** Prefixed onto every element id and the radio group name, so two panels can share a document. */
  @Input() idPrefix = 'yaml-import';
  @Input() preferredSource: ImportSource = 'paste';
  @Input() expectation: ImportExpectation | null = null;
  /** The file the agent writes; named in the advice when the downloaded file is attached instead. */
  @Input() expectedFileName: string | null = null;
  /** The file downloaded for the agent, on the wizard's add route only; without it there is no advice. */
  @Input() downloadedFileName: string | null = null;

  @Output() imported = new EventEmitter<ImportBenchmarkQuestionsResultDto>();
  @Output() suiteImported = new EventEmitter<BenchmarkSuiteDto>();
  @Output() helpRequested = new EventEmitter<void>();
  @Output() stateChange = new EventEmitter<ImportPanelState>();

  readonly formatDifficulty = formatDifficulty;
  readonly labels = PANEL_LABELS;

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
  private parsed: ParseResult | null = null;
  private plan: ImportPlanItem[] = [];

  cards: ReviewCard[] = [];
  suiteName = '';
  suiteDescription: string | null = null;
  /** `suite.suggested_description` of the validated document; null when it carries none. */
  suggestedDescription: string | null = null;
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

  /** Route checks for the validated document; empty without an expectation. */
  findings: ExpectationFinding[] = [];
  confirmed = false;

  applying = false;
  applyError: string | null = null;
  doneSummary = '';
  intentCheck: ImportIntentCheck | null = null;
  /** The BOARD FACTS quote check for the imported or newly attached board; null while none applies. */
  boardFactsCheck: BoardFactsCheckDto | null = null;
  private boardFactsCheckSub: Subscription | undefined;

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

  get blockingFindings(): ExpectationFinding[] {
    return this.findings.filter(f => f.level === 'blocking');
  }

  get warningFindings(): ExpectationFinding[] {
    return this.findings.filter(f => f.level === 'warning');
  }

  get confirmedFindings(): ExpectationFinding[] {
    return this.findings.filter(f => f.level === 'confirmed');
  }

  get boardChecking(): boolean {
    return !!this.expectation && this.snapshotCheckState === 'loading';
  }

  get canApply(): boolean {
    if (this.applying || this.cards.length === 0) return false;
    if (!this.expectation) return true;
    return this.blockingFindings.length === 0 && this.confirmed;
  }

  get applyLabel(): string {
    if (this.expectation) {
      return this.expectation.route === 'add-to-suite'
        ? PANEL_LABELS.addQuestions(this.cards.length, this.suite?.name ?? 'the suite')
        : PANEL_LABELS.createSuite(this.resolvedSuiteName);
    }
    if (this.mode === 'single') return 'Replace question';
    if (this.mode === 'suite') return 'Create suite';
    return `Apply ${this.changeCount} ${this.changeCount === 1 ? 'change' : 'changes'}`;
  }

  get confirmLabel(): string {
    const warnings = this.warningFindings.length;
    const read = warnings > 0 ? ` and read the ${warnings === 1 ? 'warning' : `${warnings} warnings`}` : '';
    return this.expectation?.route === 'add-to-suite'
      ? `I have reviewed the questions${read} and want to add them to ${this.suite?.name ?? 'the suite'}`
      : `I have reviewed the questions${read} and want to create this suite`;
  }

  /** The name the server will give an imported suite, following its "(Imported)" collision rule. */
  get resolvedSuiteName(): string {
    return resolveImportedSuiteName(this.suiteName, this.suites.map(s => s.name));
  }

  /** Route A: the empty-list error on a file that still carries a board is the downloaded file. */
  get showDownloadedFileHint(): boolean {
    return this.expectation?.route === 'add-to-suite'
      && !!this.parsed?.suite?.snapshot
      && this.errors.some(e => e.message === EMPTY_QUESTIONS_ERROR);
  }

  readonly downloadedFileHint = DOWNLOADED_FILE_HINT;

  // ---- Step 1 --------------------------------------------------------------------------------

  setSource(source: ImportSource): void {
    this.source = source;
    this.clearValidation();
    this.changed();
  }

  onPastedTextChange(text: string): void {
    this.pastedText = text;
    this.clearValidation();
    this.stateChange.emit(this.state);
  }

  async loadFile(file: File): Promise<void> {
    this.fileError = null;
    if (file.size > MAX_IMPORT_FILE_BYTES) {
      this.fileText = null;
      this.fileName = '';
      this.fileSize = 0;
      this.fileError = `${file.name} is ${formatBytes(file.size)}; the limit is 2 MB.`;
      this.clearValidation();
      this.changed();
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
    this.changed();
  }

  clearFile(): void {
    this.fileText = null;
    this.fileName = '';
    this.fileSize = 0;
    this.fileError = null;
    this.clearValidation();
    this.changed();
  }

  get fileSizeLabel(): string {
    return formatBytes(this.fileSize);
  }

  /**
   * Advice when the attached file is, by its name, the one downloaded for the agent. Advisory only:
   * a renamed file is still a valid file, and the content checks on validation remain the gate.
   */
  get fileNameAdvice(): string | null {
    if (this.source !== 'file' || this.fileText === null || !this.downloadedFileName) {
      return null;
    }
    const looksDownloaded = isSuiteExportFileName(this.fileName) || this.fileName === this.downloadedFileName;
    if (!looksDownloaded) return null;
    return this.expectedFileName
      ? `This is the file you downloaded for the agent. Upload ${this.expectedFileName} instead.`
      : 'This is the file you downloaded for the agent. Upload the file the agent wrote instead.';
  }

  async validate(): Promise<boolean> {
    const text = this.currentText;
    if (text.trim() === '') {
      this.errors = [{ line: null, message: this.source === 'paste' ? 'Paste a YAML document first.' : 'Choose a file first.' }];
      this.changed();
      return false;
    }

    this.validating = true;
    this.changed();

    const parsed = await parseQuestionYaml(text);
    // With an expectation the route checks name a different suite instead of this notice.
    const modeResult = validateForMode(parsed, this.mode, this.existing, this.target ?? undefined,
      this.expectation ? null : this.suite?.name);

    this.validating = false;
    /* The text changed while the parser ran; this verdict belongs to text that is no longer there. */
    if (text !== this.currentText) {
      this.changed();
      return false;
    }

    this.parsed = parsed;
    this.errors = [...parsed.errors, ...modeResult.errors];
    this.notices = [...parsed.notices, ...modeResult.notices];

    if (this.errors.length > 0) {
      this.validatedText = null;
      this.validSummary = null;
      this.cards = [];
      this.plan = [];
      this.findings = [];
      this.changed();
      return false;
    }

    this.suiteSnapshot = this.mode === 'suite' ? toSuiteSnapshot(parsed) : null;
    this.fileSha256 = parsed.suite?.snapshot?.sha256 ?? null;
    this.attachSnapshot = true;
    this.confirmed = false;
    this.cancelSnapshotCheck();

    const plan = buildImportPlan(parsed, this.mode, this.existing, this.target ?? undefined);
    this.plan = plan;
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
    this.suggestedDescription = parsed.suite?.suggestedDescription ?? null;
    this.validatedText = text;
    this.validSummary = this.describeValid(plan);
    this.refreshFindings();
    this.changed();
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
    this.enterReview();
  }

  /** One step for a host with a single forward button: validate, and review when it passes. */
  async validateAndReview(): Promise<boolean> {
    if (this.validating) return false;
    if (!(await this.validate())) return false;
    this.enterReview();
    return true;
  }

  private enterReview(): void {
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
    this.changed();
  }

  onConfirmedChange(confirmed: boolean): void {
    this.confirmed = confirmed;
    this.changed();
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

  /** The name the attached snapshot will carry, for the outcome statement of route B. */
  get snapshotDisplayName(): string {
    return this.suiteSnapshot?.name || this.resolvedSuiteName;
  }

  /*
   * One preflight per validated document; it writes nothing, and the import decides again anyway.
   * Suite mode runs it for the snapshot card; with an expectation, route A runs it for the board check.
   */
  private checkSnapshot(): void {
    this.cancelSnapshotCheck();
    const text = this.mode === 'suite'
      ? this.suiteSnapshot?.text
      : this.expectation?.route === 'add-to-suite' ? this.parsed?.suite?.snapshot?.text : undefined;
    if (!text) {
      return;
    }
    this.snapshotCheckState = 'loading';
    this.snapshotCheckSub = this.benchmarkService.matchSnapshot(text).subscribe({
      next: result => {
        this.snapshotCheck = result;
        this.snapshotCheckState = 'ready';
        this.refreshFindings();
        this.changed();
      },
      error: () => {
        this.snapshotCheck = null;
        this.snapshotCheckState = 'failed';
        this.refreshFindings();
        this.changed();
      }
    });
  }

  private cancelSnapshotCheck(): void {
    this.snapshotCheckSub?.unsubscribe();
    this.snapshotCheckSub = undefined;
    this.snapshotCheck = null;
    this.snapshotCheckState = 'idle';
  }

  private refreshFindings(): void {
    if (!this.expectation || !this.parsed) {
      this.findings = [];
      return;
    }
    const board = boardIdentityOf(this.parsed, this.expectation.targetSuite, this.snapshotCheck);
    const findings = checkImportExpectation(this.parsed, this.plan, this.expectation, board, this.suites.map(s => s.name));
    // While the preflight runs the board is not unchecked, only not checked yet.
    this.findings = this.snapshotCheckState === 'loading' || this.snapshotCheckState === 'idle'
      ? findings.filter(f => f.code !== 'board-unchecked')
      : findings;
  }

  requestHelp(): void {
    this.helpRequested.emit();
  }

  // ---- Step 2 --------------------------------------------------------------------------------

  setView(view: ReviewView): void {
    this.view = view;
    this.changed();
  }

  back(): void {
    if (this.applying) return;
    this.applyError = null;
    this.cancelSnapshotCheck();
    this.goToStep(1);
  }

  apply(): void {
    if (this.applying || this.cards.length === 0) return;
    if (this.expectation && !this.canApply) return;
    this.applying = true;
    this.applyError = null;
    this.changed();

    const items = toImportItems(this.cards.map(c => c.item), this.mode);
    const expected = this.cards.length;

    if (this.mode === 'suite') {
      // The wizard's create route always attaches the board; its checkbox is not offered there.
      const snapshot = this.attachSnapshot || this.expectation ? this.suiteSnapshot : null;
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
          if (this.expectation) {
            const ok = suite.questionCount === expected && suite.gameSnapshotId != null;
            this.intentCheck = ok
              ? { ok, message: `As intended: the new suite holds ${expected} ${expected === 1 ? 'question' : 'questions'} and has its game snapshot.` }
              : { ok, message: `Expected ${expected} questions and an attached game snapshot; the server reports ${suite.questionCount} questions and ${suite.gameSnapshotId != null ? 'a snapshot' : 'no snapshot'}.` };
          }
          this.suiteImported.emit(suite);
          this.goToStep(3);
          this.fetchBoardFactsCheck(suite.id);
        },
        error: err => this.onApplyError(err)
      });
      return;
    }

    if (!this.suite) {
      this.applying = false;
      this.applyError = 'No suite is open.';
      this.changed();
      return;
    }

    this.benchmarkService.importQuestions(this.suite.id, { items }).subscribe({
      next: result => {
        this.applying = false;
        this.doneSummary = `Replaced ${result.replacedCount}, created ${result.createdCount}, unchanged ${result.unchangedCount}.`;
        if (this.expectation) {
          const ok = result.createdCount === expected && result.replacedCount === 0;
          this.intentCheck = ok
            ? { ok, message: `As intended: created ${expected}, replaced 0.` }
            : { ok, message: `Expected to create ${expected} and replace 0; the server reports created ${result.createdCount}, replaced ${result.replacedCount}.` };
        }
        this.boardFactsCheck = result.boardFactsCheck ?? null;
        this.imported.emit(result);
        this.goToStep(3);
      },
      error: err => this.onApplyError(err)
    });
  }

  /** Suite mode's `importSuite` carries no check of its own; the done step fetches it on demand. */
  private fetchBoardFactsCheck(suiteId: number): void {
    this.boardFactsCheckSub?.unsubscribe();
    this.boardFactsCheckSub = this.benchmarkService.getBoardFactsCheck(suiteId).subscribe({
      next: check => {
        this.boardFactsCheck = check;
        this.changed();
      },
      error: () => { /* Advisory only; the done step still reports the import outcome. */ }
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
    this.changed();
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

  // ---- Host API ------------------------------------------------------------------------------

  get state(): ImportPanelState {
    return {
      step: this.step,
      validating: this.validating,
      applying: this.applying,
      canValidate: this.canValidate,
      canReview: this.canReview,
      canApply: this.canApply
    };
  }

  focusStepHeading(): void {
    this.stepHeading?.nativeElement.focus({ preventScroll: true });
  }

  reset(): void {
    this.step = 1;
    this.source = this.preferredSource;
    this.pastedText = '';
    this.fileText = null;
    this.fileName = '';
    this.fileSize = 0;
    this.fileError = null;
    this.validating = false;
    this.clearValidation();
    this.cards = [];
    this.plan = [];
    this.parsed = null;
    this.findings = [];
    this.confirmed = false;
    this.suiteName = '';
    this.suiteDescription = null;
    this.suggestedDescription = null;
    this.suiteSnapshot = null;
    this.fileSha256 = null;
    this.attachSnapshot = true;
    this.cancelSnapshotCheck();
    this.view = 'side';
    this.applying = false;
    this.applyError = null;
    this.doneSummary = '';
    this.intentCheck = null;
    this.boardFactsCheck = null;
    this.boardFactsCheckSub?.unsubscribe();
    this.boardFactsCheckSub = undefined;
    this.changed();
  }

  /** Abandons a preflight in flight; the host calls it when it closes. */
  cancel(): void {
    this.cancelSnapshotCheck();
    this.boardFactsCheckSub?.unsubscribe();
  }

  ngOnDestroy(): void {
    this.cancelSnapshotCheck();
    this.boardFactsCheckSub?.unsubscribe();
  }

  // ---- Internals -----------------------------------------------------------------------------

  private goToStep(step: ImportStep): void {
    this.step = step;
    this.changed();
    this.focusStepHeading();
  }

  private clearValidation(): void {
    this.errors = [];
    this.notices = [];
    this.validSummary = null;
    this.validatedText = null;
  }

  private changed(): void {
    this.cdr.detectChanges();
    this.stateChange.emit(this.state);
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
