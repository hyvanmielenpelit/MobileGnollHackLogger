import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CodeBlockComponent } from '../../../shared/code-block/code-block.component';
import { MAX_SUITE_NAME_LENGTH } from './question-yaml-format';
import {
  DEFAULT_BAND_COUNT,
  MAX_QUESTIONS_PER_SUITE,
  SuiteAgentPromptField,
  SuiteAgentPromptOptions,
  SuiteAgentPromptSource,
  agentOutputFileName,
  buildSuiteAgentPrompt,
  validateSuiteAgentPromptOptions
} from './suite-agent-prompt';
import { SUITE_AI_PROMPT_FILE_NAME } from './suite-yaml-guide';
import { BUILDER_LABELS } from './suite-workflow-instructions';

/** The download name of a prompt that adds questions to an existing suite. */
export const SUITE_ADD_QUESTIONS_PROMPT_FILE_NAME = 'overseer-suite-add-questions-prompt.md';

/**
 * The prompt step of the Snapshot Suite Wizard: the fields of {@link SuiteAgentPromptOptions} the
 * wizard does not own, a Generate button, and the assembled prompt in a copyable code block.
 * The route and the path are inputs; changing either discards a generated prompt.
 */
@Component({
  selector: 'app-suite-prompt-builder',
  standalone: true,
  imports: [CommonModule, FormsModule, CodeBlockComponent],
  templateUrl: './suite-prompt-builder.component.html',
  styleUrls: ['./suite-prompt-builder.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SuitePromptBuilderComponent implements OnChanges {
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('result') result?: ElementRef<HTMLElement>;

  /** Prefixed onto every element id, so two instances can share one document. */
  @Input() idPrefix = 'suite-prompt';
  @Input() source: SuiteAgentPromptSource | null = null;
  @Input() sourcePath = '';
  /** Route A: the existing suite's name, which names the agent's output file. */
  @Input() knownSuiteName = '';

  /** Emits the options a prompt was generated from. */
  @Output() generated = new EventEmitter<SuiteAgentPromptOptions>();

  suiteName = '';
  countsMode: 'propose' | 'manual' = 'propose';
  simple: number | null = DEFAULT_BAND_COUNT;
  intermediate: number | null = DEFAULT_BAND_COUNT;
  advanced: number | null = DEFAULT_BAND_COUNT;
  waitForGoAhead = true;

  readonly maxSuiteNameLength = MAX_SUITE_NAME_LENGTH;
  readonly maxQuestions = MAX_QUESTIONS_PER_SUITE;
  readonly labels = BUILDER_LABELS;

  /** The generated prompt, or empty while none is current. */
  prompt = '';
  /** Messages for the fields that are currently shown as invalid. */
  errors: Partial<Record<SuiteAgentPromptField, string>> = {};
  /** The form-level announcement: generated, or invalidated by an edit. */
  status = '';

  get isSuiteYaml(): boolean {
    return this.source === 'suite-yaml';
  }

  get options(): SuiteAgentPromptOptions {
    return {
      source: this.source,
      sourcePath: this.sourcePath,
      suiteName: this.isSuiteYaml ? this.knownSuiteName : this.suiteName,
      counts: this.countsMode === 'manual'
        ? { simple: this.simple ?? NaN, intermediate: this.intermediate ?? NaN, advanced: this.advanced ?? NaN }
        : null,
      waitForGoAhead: this.waitForGoAhead
    };
  }

  get total(): number {
    return (this.simple ?? 0) + (this.intermediate ?? 0) + (this.advanced ?? 0);
  }

  get outputFileName(): string {
    return agentOutputFileName(this.source ?? 'snapshot-file', this.options.suiteName.trim());
  }

  get promptFileName(): string {
    return this.isSuiteYaml ? SUITE_ADD_QUESTIONS_PROMPT_FILE_NAME : SUITE_AI_PROMPT_FILE_NAME;
  }

  /** The route or path problem the wizard should have prevented; shown above Generate. */
  get sourceError(): string | null {
    return this.errors.source ?? this.errors.sourcePath ?? null;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['source'] || changes['sourcePath'] || changes['knownSuiteName']) {
      delete this.errors.source;
      delete this.errors.sourcePath;
      this.discardPrompt();
    }
  }

  /** A generated prompt describes the fields as they were; an edit makes it a lie. */
  onFieldInput(field: SuiteAgentPromptField): void {
    delete this.errors[field];
    this.discardPrompt();
  }

  onFieldBlur(field: SuiteAgentPromptField): void {
    const message = validateSuiteAgentPromptOptions(this.options)[field];
    if (message) {
      this.errors[field] = message;
    } else {
      delete this.errors[field];
    }
    this.cdr.detectChanges();
  }

  setCountsMode(mode: 'propose' | 'manual'): void {
    this.countsMode = mode;
    delete this.errors.counts;
    this.discardPrompt();
    this.cdr.detectChanges();
  }

  setWaitForGoAhead(value: boolean): void {
    this.waitForGoAhead = value;
    this.discardPrompt();
    this.cdr.detectChanges();
  }

  generate(): void {
    const options = this.options;
    this.errors = validateSuiteAgentPromptOptions(options);
    const invalid = Object.keys(this.errors) as SuiteAgentPromptField[];
    if (invalid.length > 0) {
      this.prompt = '';
      this.status = '';
      this.cdr.detectChanges();
      this.focusFirstInvalid(invalid);
      return;
    }

    this.prompt = buildSuiteAgentPrompt(options);
    this.status = 'Prompt generated.';
    this.cdr.detectChanges();
    this.generated.emit(options);
    this.scrollResultIntoView();
  }

  private discardPrompt(): void {
    if (this.prompt !== '') {
      this.prompt = '';
      this.status = 'Inputs changed — generate the prompt again.';
    }
  }

  private focusFirstInvalid(invalid: SuiteAgentPromptField[]): void {
    const order: SuiteAgentPromptField[] = ['suiteName', 'counts'];
    const first = order.find(f => invalid.includes(f));
    const id = first === 'suiteName' ? 'name' : first === 'counts' ? 'simple' : 'generate';
    document.getElementById(`${this.idPrefix}-${id}`)?.focus();
  }

  private scrollResultIntoView(): void {
    const reduced = typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
      : false;
    this.result?.nativeElement.scrollIntoView?.({ block: 'nearest', behavior: reduced ? 'auto' : 'smooth' });
  }
}
