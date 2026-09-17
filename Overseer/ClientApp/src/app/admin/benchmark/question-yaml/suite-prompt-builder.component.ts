import { ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, Input, OnDestroy, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { downloadTextFile } from '../../../utils/download.util';
import { MAX_SUITE_NAME_LENGTH, suiteSlug } from './question-yaml-format';
import {
  DEFAULT_BAND_COUNT,
  MAX_QUESTIONS_PER_SUITE,
  SuiteAgentPromptField,
  SuiteAgentPromptOptions,
  buildSuiteAgentPrompt,
  looksLikeAbsolutePath,
  validateSuiteAgentPromptOptions
} from './suite-agent-prompt';
import { SUITE_AI_PROMPT_FILE_NAME } from './suite-yaml-guide';

const STATUS_MS = 3000;

/**
 * The *AI Prompt* tab of the suite help: the fields of {@link SuiteAgentPromptOptions}, a Generate
 * button, and the assembled prompt with Copy and Download.
 *
 * Nothing is persisted and nothing is fetched. The host renders this inside `@if`, so a tab switch
 * destroys it and the next visit starts empty, which is intended.
 */
@Component({
  selector: 'app-suite-prompt-builder',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './suite-prompt-builder.component.html',
  styleUrls: ['./suite-prompt-builder.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SuitePromptBuilderComponent implements OnDestroy {
  private cdr = inject(ChangeDetectorRef);

  @ViewChild('result') result?: ElementRef<HTMLElement>;

  /** Prefixed onto every element id, so two instances can share one document. */
  @Input() idPrefix = 'suite-prompt';

  snapshotPath = '';
  suiteName = '';
  countsMode: 'propose' | 'manual' = 'propose';
  simple: number | null = DEFAULT_BAND_COUNT;
  intermediate: number | null = DEFAULT_BAND_COUNT;
  advanced: number | null = DEFAULT_BAND_COUNT;
  waitForGoAhead = true;

  readonly maxSuiteNameLength = MAX_SUITE_NAME_LENGTH;
  readonly maxQuestions = MAX_QUESTIONS_PER_SUITE;

  /** The generated prompt, or empty while none is current. */
  prompt = '';
  /** Messages for the fields that are currently shown as invalid. */
  errors: Partial<Record<SuiteAgentPromptField, string>> = {};
  /** The form-level announcement: generated, or invalidated by an edit. */
  status = '';
  /** The copy toolbar's announcement. */
  copyStatus = '';

  private statusTimer: ReturnType<typeof setTimeout> | undefined;

  get options(): SuiteAgentPromptOptions {
    return {
      snapshotPath: this.snapshotPath,
      suiteName: this.suiteName,
      counts: this.countsMode === 'manual'
        ? { simple: this.simple ?? NaN, intermediate: this.intermediate ?? NaN, advanced: this.advanced ?? NaN }
        : null,
      waitForGoAhead: this.waitForGoAhead
    };
  }

  get total(): number {
    return (this.simple ?? 0) + (this.intermediate ?? 0) + (this.advanced ?? 0);
  }

  /** Advisory only: a relative path still resolves, against the agent's working directory. */
  get showRelativePathHint(): boolean {
    return this.snapshotPath.trim() !== '' && !looksLikeAbsolutePath(this.snapshotPath);
  }

  get outputFileName(): string {
    return `benchmark-suite-${suiteSlug(this.suiteName.trim())}.yaml`;
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
    this.copyStatus = '';
    this.cdr.detectChanges();
    this.scrollResultIntoView();
  }

  async copyPrompt(): Promise<void> {
    const ok = await copyToClipboard(this.prompt);
    this.copyStatus = ok ? 'Copied' : 'Could not copy; use Download instead.';
    this.cdr.detectChanges();
    clearTimeout(this.statusTimer);
    this.statusTimer = setTimeout(() => {
      this.copyStatus = '';
      this.cdr.detectChanges();
    }, STATUS_MS);
  }

  downloadPrompt(): void {
    downloadTextFile(SUITE_AI_PROMPT_FILE_NAME, this.prompt, 'text/markdown;charset=utf-8');
  }

  ngOnDestroy(): void {
    clearTimeout(this.statusTimer);
  }

  private discardPrompt(): void {
    if (this.prompt !== '') {
      this.prompt = '';
      this.copyStatus = '';
      this.status = 'Inputs changed — generate the prompt again.';
    }
  }

  private focusFirstInvalid(invalid: SuiteAgentPromptField[]): void {
    const order: SuiteAgentPromptField[] = ['snapshotPath', 'suiteName', 'counts'];
    const first = order.find(f => invalid.includes(f));
    const suffix = first === 'snapshotPath' ? 'path' : first === 'suiteName' ? 'name' : 'simple';
    document.getElementById(`${this.idPrefix}-${suffix}`)?.focus();
  }

  private scrollResultIntoView(): void {
    const reduced = typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
      : false;
    this.result?.nativeElement.scrollIntoView({ block: 'nearest', behavior: reduced ? 'auto' : 'smooth' });
  }
}
