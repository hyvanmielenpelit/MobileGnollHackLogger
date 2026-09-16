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
import { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  GenerateSuiteDescriptionRequest,
  SuiteDescriptionGenerationResultDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { copyToClipboard } from '../../../utils/clipboard.util';
import {
  formatPickerPrice,
  formatServiceTier,
  formatThinkingLevel,
  showReasoningBadge
} from '../../../utils/model-badge-format.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { MarkdownPipe } from '../../../chat/markdown.pipe';

/**
 * The operator instructions a new generation starts with. Must stay byte-identical to
 * `BenchmarkDescriptionPrompt.DefaultInstructions` on the server.
 */
export const DEFAULT_SUITE_DESCRIPTION_INSTRUCTIONS =
  'Write a Markdown description of this benchmark suite for the administrators who choose and run suites, 120 to 300 words long. ' +
  'Start with one lead paragraph that states what the suite tests, who it is for, and its difficulty spread, giving the number of questions in each difficulty band. ' +
  'Follow it with a `### Covered Domains` heading and a bullet list in which each bullet opens with a bolded domain group name, then a colon and the topics that group covers, for example `- **Magic & Mechanics**: spell schools, prayer timeouts, and sacrifice gifts.` ' +
  'Use no heading above `###`. Do not quote any question verbatim, and do not reveal or hint at any answer. ' +
  'Output the Markdown only, with no preamble and no closing remarks.';

/** The dialog's own outcome of the last (or in-flight) generation request. */
export type SuiteDescriptionGenerationStatus = 'Idle' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';

/** Which pane of the generated description is shown: the source, or the rendered HTML. */
export type SuiteDescriptionResultMode = 'markdown' | 'preview';

/**
 * A single synchronous model call that drafts a suite description from its questions and,
 * optionally, its game snapshot. Nothing is saved here: the result lands in the description
 * editor of the Edit Suite dialog and is persisted only when the operator saves that suite.
 *
 * The host owns `visible`. `closed` fires on every close path; `descriptionGenerated` fires only
 * when the operator picks *Use this description*, carrying the Markdown to apply.
 */
@Component({
  selector: 'app-suite-description-generation-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, ProviderBadgeComponent, MarkdownPipe],
  templateUrl: './suite-description-generation-dialog.component.html',
  styleUrls: ['./suite-description-generation-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SuiteDescriptionGenerationDialogComponent implements OnInit, OnChanges, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  static readonly COPIED_RESET_MS = 2000;
  static readonly TICK_MS = 1000;

  @Input() suiteId: number | null = null;
  @Input() suiteName = '';
  @Input() questionCount = 0;
  @Input() gameSnapshotName: string | null = null;
  @Input() visible = false;
  @Input() benchmarkCapableConfigs: SystemAiConfigDto[] = [];
  @Input() defaultModelConfigId: number | null = null;
  @Input() overseerBuildVersion: string | null = null;

  /** Escape, the header close button, Close, and Discard on the unapplied-result confirmation. */
  @Output() closed = new EventEmitter<void>();
  /** The generated Markdown, once *Use this description* is picked. Closing follows immediately. */
  @Output() descriptionGenerated = new EventEmitter<string>();

  @ViewChild('dialog', { static: true }) dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('heading', { static: true }) heading?: ElementRef<HTMLElement>;
  @ViewChild('confirmDialog', { static: true }) confirmDialog?: ElementRef<HTMLDialogElement>;

  readonly formatThinkingLevel = formatThinkingLevel;
  readonly showReasoningBadge = showReasoningBadge;
  readonly formatServiceTier = formatServiceTier;
  readonly formatPickerPrice = formatPickerPrice;

  // --- Setup ---------------------------------------------------------------------------------
  modelConfigId: number | null = null;
  includeSnapshot = true;
  includeDebugText = false;
  instructions = DEFAULT_SUITE_DESCRIPTION_INSTRUCTIONS;
  isModelDropdownOpen = false;

  // --- Run -----------------------------------------------------------------------------------
  status: SuiteDescriptionGenerationStatus = 'Idle';
  result: SuiteDescriptionGenerationResultDto | null = null;
  dialogError: string | null = null;
  /** Set once *Use this description* has emitted the result; guards the close confirmation. */
  applied = false;
  elapsedMs = 0;
  readonly resultModes: SuiteDescriptionResultMode[] = ['markdown', 'preview'];
  resultMode: SuiteDescriptionResultMode = 'preview';

  // --- Diagnostics ---------------------------------------------------------------------------
  diagnosticsOpen = false;
  copiedDiagnostics = false;
  diagnosticsCopyFailed = false;

  private isOpen = false;
  private subscription: Subscription | null = null;
  private timerId: ReturnType<typeof setInterval> | null = null;
  private copiedTimer: ReturnType<typeof setTimeout> | null = null;
  private startedAtMs = 0;
  private lastHttpErrorStatus: number | null = null;
  private lastHttpErrorBody: string | null = null;

  ngOnInit(): void {
    // The diagnostics copy button carries an interestfor + popover="hint" tooltip.
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['visible']) {
      if (this.visible) {
        if (!this.isOpen) {
          this.openDialog();
        }
      } else if (this.isOpen) {
        // The host lowered `visible`; close without echoing a close request back.
        this.closeDialog();
      }
    }

    if (changes['benchmarkCapableConfigs'] && this.isOpen && this.modelConfigId == null) {
      this.applyDefaultModel();
      this.cdr.markForCheck();
    }
  }

  ngOnDestroy(): void {
    this.stopSubscription();
    this.stopTimer();
    if (this.copiedTimer) {
      clearTimeout(this.copiedTimer);
      this.copiedTimer = null;
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    if (this.isModelDropdownOpen && !target?.closest('.sdg-model-selector')) {
      this.isModelDropdownOpen = false;
      this.cdr.markForCheck();
    }
  }

  // -------------------------------------------------------------------------------------------
  // Dialog lifecycle
  // -------------------------------------------------------------------------------------------

  private openDialog(): void {
    this.isOpen = true;
    this.resetState();
    this.dialog?.nativeElement.showModal();
    this.cdr.detectChanges();
    this.heading?.nativeElement.focus();
  }

  private resetState(): void {
    this.stopSubscription();
    this.stopTimer();
    this.status = 'Idle';
    this.result = null;
    this.dialogError = null;
    this.applied = false;
    this.elapsedMs = 0;
    this.resultMode = 'preview';
    this.diagnosticsOpen = false;
    this.copiedDiagnostics = false;
    this.diagnosticsCopyFailed = false;
    this.isModelDropdownOpen = false;
    this.includeSnapshot = true;
    this.includeDebugText = false;
    this.instructions = DEFAULT_SUITE_DESCRIPTION_INSTRUCTIONS;
    this.lastHttpErrorStatus = null;
    this.lastHttpErrorBody = null;
    this.applyDefaultModel();
  }

  private applyDefaultModel(): void {
    const configs = this.benchmarkCapableConfigs ?? [];
    if (this.defaultModelConfigId != null && configs.some(c => c.id === this.defaultModelConfigId)) {
      this.modelConfigId = this.defaultModelConfigId;
    } else {
      this.modelConfigId = configs[0]?.id ?? null;
    }
  }

  /** Closes without telling the host; used when the host itself lowered `visible`. */
  private closeDialog(): void {
    this.isOpen = false;
    this.stopSubscription();
    this.stopTimer();
    this.isModelDropdownOpen = false;
    if (this.confirmDialog?.nativeElement.open) {
      this.confirmDialog.nativeElement.close();
    }
    this.dialog?.nativeElement.close();
    this.cdr.markForCheck();
  }

  private close(): void {
    this.closeDialog();
    this.closed.emit();
  }

  /**
   * Escape, the header close button and the footer Close button all land here. A running request
   * is aborted and the dialog closes without a prompt; a completed description that was never
   * applied asks first; anything else closes immediately.
   */
  requestClose(): void {
    if (!this.isOpen) return;
    if (this.status === 'Completed' && !this.applied) {
      this.openDiscardConfirm();
      return;
    }
    this.close();
  }

  /** Bound to the main dialog's `(cancel)`, i.e. Escape, so it goes through the same guard. */
  onDialogCancel(event: Event): void {
    event.preventDefault();
    this.requestClose();
  }

  // -------------------------------------------------------------------------------------------
  // Unapplied-result confirmation
  // -------------------------------------------------------------------------------------------

  private openDiscardConfirm(): void {
    this.cdr.detectChanges();
    this.confirmDialog?.nativeElement.showModal();
  }

  closeDiscardConfirm(): void {
    if (this.confirmDialog?.nativeElement.open) {
      this.confirmDialog.nativeElement.close();
    }
    this.cdr.markForCheck();
  }

  discardAndClose(): void {
    this.closeDiscardConfirm();
    this.close();
  }

  // -------------------------------------------------------------------------------------------
  // Model selector
  // -------------------------------------------------------------------------------------------

  toggleModelDropdown(event: Event): void {
    event.stopPropagation();
    if (this.status === 'Running') return;
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

  // -------------------------------------------------------------------------------------------
  // Derived state
  // -------------------------------------------------------------------------------------------

  get running(): boolean {
    return this.status === 'Running';
  }

  get isTerminal(): boolean {
    return this.status === 'Completed' || this.status === 'Failed' || this.status === 'Cancelled';
  }

  get dialogTitle(): string {
    switch (this.status) {
      case 'Running': return 'Generating Suite Description';
      case 'Completed': return 'Suite Description Generated';
      case 'Failed': return 'Suite Description Generation Failed';
      case 'Cancelled': return 'Suite Description Generation Cancelled';
      default: return 'Generate Suite Description';
    }
  }

  get progressStatusLabel(): string {
    const name = this.selectedModel?.displayName || this.selectedModel?.modelId || 'the model';
    return `Generating with ${name}… ${this.elapsedLabel}`;
  }

  get elapsedLabel(): string {
    return this.formatElapsed(this.result ? this.result.durationMs : this.elapsedMs);
  }

  get cachedTokens(): number {
    if (!this.result) return 0;
    return (this.result.cacheReadTokens || 0) + (this.result.cacheCreationTokens || 0);
  }

  // -------------------------------------------------------------------------------------------
  // Starting, cancelling and re-running
  // -------------------------------------------------------------------------------------------

  start(): void {
    if (this.suiteId == null || this.modelConfigId == null || this.status === 'Running') return;
    this.status = 'Running';
    this.dialogError = null;
    this.result = null;
    this.applied = false;
    this.diagnosticsOpen = false;
    this.lastHttpErrorStatus = null;
    this.lastHttpErrorBody = null;
    this.startTimer();
    this.cdr.markForCheck();

    const request: GenerateSuiteDescriptionRequest = {
      generatorModelConfigurationId: this.modelConfigId,
      instructions: this.instructions.trim() || undefined,
      includeSnapshot: this.includeSnapshot,
      includeDebugText: this.includeDebugText
    };

    this.subscription = this.benchmarkService.generateSuiteDescription(this.suiteId, request).subscribe({
      next: (result) => {
        this.subscription = null;
        this.stopTimer();
        this.result = result;
        this.status = result.status === 'Completed' ? 'Completed'
          : result.status === 'Cancelled' ? 'Cancelled'
          : 'Failed';
        if (this.status === 'Failed') {
          this.diagnosticsOpen = true;
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.subscription = null;
        this.stopTimer();
        this.status = 'Failed';
        this.dialogError = this.describeError(err, 'Failed to generate a suite description.');
        this.diagnosticsOpen = true;
        this.cdr.markForCheck();
      }
    });
  }

  /** Aborts the HTTP request; the server records no usage for a call that never returned tokens. */
  cancel(): void {
    if (this.status !== 'Running') return;
    this.subscription?.unsubscribe();
    this.subscription = null;
    this.stopTimer();
    this.status = 'Cancelled';
    this.cdr.markForCheck();
  }

  /** Keeps the setup as it stands and clears the previous run so the setup is usable again. */
  generateAgain(): void {
    this.status = 'Idle';
    this.result = null;
    this.dialogError = null;
    this.applied = false;
    this.resultMode = 'preview';
    this.diagnosticsOpen = false;
    this.lastHttpErrorStatus = null;
    this.lastHttpErrorBody = null;
    this.cdr.markForCheck();
  }

  // -------------------------------------------------------------------------------------------
  // Generated description view: a two-tab row with the roving-tabindex keyboard model
  // -------------------------------------------------------------------------------------------

  resultModeLabel(mode: SuiteDescriptionResultMode): string {
    return mode === 'markdown' ? 'Markdown' : 'Preview';
  }

  selectResultMode(mode: SuiteDescriptionResultMode): void {
    this.resultMode = mode;
    this.cdr.markForCheck();
  }

  onResultTabKeydown(event: KeyboardEvent, index: number): void {
    const modes = this.resultModes;
    let next = index;
    if (event.key === 'ArrowRight') next = (index + 1) % modes.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + modes.length) % modes.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = modes.length - 1;
    else return;

    event.preventDefault();
    this.selectResultMode(modes[next]);
    this.cdr.detectChanges();
    document.getElementById(`sdgResult-tab-${this.resultMode}`)?.focus();
  }

  useDescription(): void {
    const description = this.result?.description;
    if (!description) return;
    this.applied = true;
    this.descriptionGenerated.emit(description);
    this.close();
  }

  // -------------------------------------------------------------------------------------------
  // Elapsed timer
  // -------------------------------------------------------------------------------------------

  private startTimer(): void {
    this.stopTimer();
    this.startedAtMs = Date.now();
    this.elapsedMs = 0;
    this.timerId = setInterval(() => {
      this.elapsedMs = Date.now() - this.startedAtMs;
      this.cdr.markForCheck();
    }, SuiteDescriptionGenerationDialogComponent.TICK_MS);
  }

  private stopTimer(): void {
    if (this.timerId) {
      clearInterval(this.timerId);
      this.timerId = null;
    }
  }

  private stopSubscription(): void {
    this.subscription?.unsubscribe();
    this.subscription = null;
  }

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

  onDiagnosticsToggle(event: Event): void {
    this.diagnosticsOpen = (event.target as HTMLDetailsElement).open;
  }

  // -------------------------------------------------------------------------------------------
  // Diagnostics. Ids, counts, statuses, the operator instructions and the log; the prompt and raw
  // response only when the debug checkbox was ticked; never an API key.
  // -------------------------------------------------------------------------------------------

  get diagnosticsText(): string {
    const lines: string[] = [];
    lines.push('=== SUITE DESCRIPTION GENERATION DIAGNOSTICS ===');
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

    lines.push('--- SUITE ---');
    lines.push(`Suite: ${this.suiteName || 'none'} (${this.suiteId ?? 'n/a'})`);
    lines.push(`Questions: ${this.questionCount}`);
    lines.push(`Snapshot: ${this.gameSnapshotName ?? 'none'}, included in this request: ${this.includeSnapshot}`);
    lines.push('');

    lines.push('--- GENERATOR ---');
    const selected = this.selectedModel;
    lines.push(`Setup column: ${selected ? `${selected.displayName || selected.modelId} (id ${selected.id})` : 'none selected'}`);
    if (this.result) {
      lines.push(`Used: ${this.result.generatorDisplayName ?? 'n/a'} (id ${this.result.generatorConfigId})`);
      lines.push(`Provider: ${this.result.generatorProvider ?? 'n/a'}, model: ${this.result.generatorModelId ?? 'n/a'}`);
      lines.push(`Thinking: ${this.result.generatorThinkingLevel ?? 'default'}, reasoning: ${this.result.generatorReasoningMode ?? 'default'}, `
        + `service tier requested: ${formatServiceTier(this.result.generatorServiceTier)}, actual: ${formatServiceTier(this.result.actualServiceTier)}`);
    }
    lines.push('');

    lines.push('--- INSTRUCTIONS ---');
    lines.push(this.instructions.trim() ? this.instructions.trim() : '(none)');
    lines.push('');

    lines.push('--- USAGE ---');
    if (this.result) {
      lines.push(`Status: ${this.result.status}, model calls: ${this.result.modelCalls}`);
      lines.push(`Prompt tokens: ${this.result.promptTokens} (uncached ${this.result.uncachedInputTokens}, `
        + `cache read ${this.result.cacheReadTokens}, cache write ${this.result.cacheCreationTokens})`);
      lines.push(`Output tokens: ${this.result.outputTokens}, reasoning tokens: ${this.result.reasoningTokens}`);
      lines.push(`Tokens estimated: ${this.result.tokensEstimated}`);
      lines.push(`Cost: ${this.result.costUsd != null ? '$' + this.result.costUsd.toFixed(4) : 'n/a'} (source: ${this.result.pricingSource ?? 'n/a'})`);
      lines.push(`Time to first token: ${this.result.timeToFirstTokenMs != null ? this.result.timeToFirstTokenMs + ' ms' : 'n/a'}`);
      lines.push(`Duration: ${this.formatElapsed(this.result.durationMs)}`);
    } else {
      lines.push('No result yet.');
    }
    lines.push('');

    lines.push('--- LOG ---');
    const log = this.result?.log ?? [];
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

    if (this.result?.promptText) {
      lines.push('--- PROMPT ---');
      lines.push(this.result.promptText);
      lines.push('');
    }
    if (this.result?.rawResponseText) {
      lines.push('--- RAW RESPONSE ---');
      lines.push(this.result.rawResponseText);
      lines.push('');
    }

    if (this.lastHttpErrorStatus != null || this.dialogError) {
      lines.push('--- REQUEST ---');
      if (this.lastHttpErrorStatus != null) lines.push(`HTTP status: ${this.lastHttpErrorStatus}`);
      if (this.lastHttpErrorBody) lines.push(`Body: ${this.lastHttpErrorBody}`);
      if (this.dialogError) lines.push(`Dialog error: ${this.dialogError}`);
      lines.push('');
    }

    return lines.join('\n');
  }

  get diagnosticsCopyStatus(): string {
    if (this.copiedDiagnostics) return 'Suite description diagnostics copied to clipboard';
    return this.diagnosticsCopyFailed ? 'Could not copy the suite description diagnostics to the clipboard.' : '';
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
      }, SuiteDescriptionGenerationDialogComponent.COPIED_RESET_MS);
    } else {
      this.copiedDiagnostics = false;
      this.diagnosticsCopyFailed = true;
      this.dialogError = 'Could not copy the suite description diagnostics to the clipboard.';
    }
    this.cdr.markForCheck();
  }

  private describeError(err: any, fallback: string): string {
    this.lastHttpErrorStatus = err?.status ?? null;
    const body = err?.error;
    this.lastHttpErrorBody = typeof body === 'string' ? body : (body != null ? JSON.stringify(body) : null);
    const httpStatus = err?.status ? ` (HTTP ${err.status})` : '';
    const msg = typeof body === 'string' && body
      ? body
      : (body?.error || body?.message || err?.message || fallback);
    return `${msg}${httpStatus}`;
  }
}
