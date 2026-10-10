import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { BenchmarkModelBatchFindingDto } from '../../../../services/admin-benchmark.service';
import { InfoTipComponent } from '../../../../shared/info-tip/info-tip.component';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';

/** A warning's *I understand* checkbox changed. */
export interface ModelBatchAcknowledgment {
  key: string;
  acknowledged: boolean;
}

/** What the card says overall: something blocks Start, a warning awaits its acknowledgment, or neither. */
export type ModelBatchReadinessState = 'blocked' | 'review' | 'ready';

/**
 * The rationale each guardrail's info tip shows when the finding carries none of its own, keyed by
 * code. Two or three sentences each; the finding's own title and sentence stay on the card.
 */
export const MODEL_BATCH_FINDING_RATIONALE: Readonly<Record<string, string>> = {
  'MB-B01': 'A batch compares models under one set of graders and settings. With one model there is nothing to compare; One model runs it directly.',
  'MB-B02': 'The limit keeps one batch within a day of runs and within what Model Comparison opens at once. Two batches under the same graders stay comparable.',
  'MB-B03': 'A model grading its own answers is not an independent judge, and models prefer their own answers. A scoring grader must differ from every model under test.',
  'MB-B04': 'A report written by the model it reports on is not independent. Choose a writer that is not a model under test, or None.',
  'MB-B05': 'A panel balances two model families against each other, so its two members must come from different providers.',
  'MB-B06': 'The launcher refuses this model as it would refuse a single run of it. Remove it from the batch or fix its configuration.',
  'MB-B07': 'Scores are weighted by assessed difficulty, so every question of the target needs one before any run starts.',
  'MB-B08': 'Each launch is checked against the run caps as it starts. A batch above a cap needs Wait when the run cap blocks the next run, or fewer runs.',
  'MB-B09': 'The server runs one benchmark at a time, so runs never share a provider\'s load and their speed figures stay clean.',
  'MB-W01': 'Graders favor their own family. With candidates from several providers, one assessor biases the ranking itself; a two-family panel balances it.',
  'MB-W02': 'The reference reader and the claim verifier are anchors meant to be neutral. On a model\'s own run, their findings are not independent.',
  'MB-W03': 'The verifier checks the claims graders call false, and from scoring method 13 it settles a critical error only one panel member raised.',
  'MB-W04': 'The highest effort levels made graders slower and broke their JSON in earlier runs, with no gain in grading. Medium is recommended.',
  'MB-W05': 'Parallel mode, service tier, endpoint and output limit are compared along with the model. When they differ, the comparison measures them too.',
  'MB-W06': 'Two identical configurations measure one model twice. Replicates belong in Runs per model, where they are pooled.',
  'MB-W07': 'The production chat answers in the concise style. Detailed results compare only with other detailed runs.',
  'MB-W08': 'A writer may describe its own provider\'s models more favorably. The documents grade nothing, but a neutral writer is safer.',
  'MB-W09': 'Spend and time are your decision. The projection is advisory, and every launch is still checked against the caps.',
  'MB-W10': 'An assessor tends to favor its own provider\'s models. The batch asks once for every member it grades, rather than once per run.',
  'MB-W11': 'The rolling 24-hour window has fewer launches left than the batch plans. With Wait checked it pauses at the cap; without, it stops and can be continued.',
  'MB-W12': 'Short runs can launch faster than the hourly cap allows. The batch then pauses until the hour frees up.',
  'MB-A01': 'One run is one sample, and run-to-run noise of a point or two is common. Two or three runs per model give an interval to rank by.',
  'MB-A02': 'The same family bias applies to every candidate, so their order is fair. Their absolute scores may still read high.',
  'MB-A03': 'Reference checks are most useful from a family that is neither a candidate nor a panel member.',
  'MB-A04': 'Model Comparison estimates a panel\'s family bias against the reference reader. Without one it cannot.',
  'MB-A05': 'The panel score is the mean of two peers. Members at different effort weigh unequally.',
  'MB-A06': 'Players get Disallowed unless they turn the references on, so Allowed measures a chat most of them do not see.',
  'MB-A07': 'Without a price, that model\'s cost per question is missing from the comparison and the projection leaves it out.',
  'MB-A08': 'A deliberating model measured against an interactive speed target scores low on Speed Index for its class, not for its own speed.',
  'MB-A09': 'A current production model in the batch gives the others a baseline measured on the same day under the same graders.',
  'MB-A10': 'Runs compare only under the same graders, so this batch starts a new comparison for these models.',
  'MB-A11': 'Each member gets its own documents. One document over the whole batch comes from Model Comparison once it ends.',
  'MB-A12': 'A second reader that sees the first verdict tends to agree with it. A blind one is an independent check.',
  'MB-T01': 'Provider speed varies by the hour. A random order keeps any model from always getting the same hours.',
  'MB-T02': 'Provider latency changes over the day, so the speed figures of models run hours apart include the hour.',
  'MB-T03': 'Chat Consistency compares speed within 4-hour UTC blocks, so a start in another block puts this run in another stratum.',
  'MB-T04': 'A corpus change mid-batch changes the instrument, and the batch stops to keep its members comparable.'
};

/** The code's leading token, upper-cased: `MB-W01 MixedFamiliesSingleAssessor` reads as `MB-W01`. */
export function modelBatchFindingCode(finding: Pick<BenchmarkModelBatchFindingDto, 'code'>): string {
  return (finding.code ?? '').trim().split(/\s+/)[0].toUpperCase();
}

/**
 * The Batch Readiness card above Start Model Batch: one summary row with a status word and a count
 * chip per severity, then the findings in a disclosure that is open while anything blocks or awaits
 * acknowledgment. Each line has its severity in word and glyph, its title and sentence, a click-mode
 * info tip with the rationale, Go to field, and for a warning an *I understand* checkbox. Advice is
 * nested in its own closed disclosure. Presentational: the host owns the findings and the
 * acknowledgments, and the server decides every finding.
 */
@Component({
  selector: 'app-model-batch-readiness',
  standalone: true,
  imports: [NgTemplateOutlet, InfoTipComponent],
  templateUrl: './model-batch-readiness.component.html',
  styleUrl: './model-batch-readiness.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModelBatchReadinessComponent implements OnInit, OnChanges {
  /** The server's findings, in its order. */
  @Input() findings: readonly BenchmarkModelBatchFindingDto[] = [];

  /** The acknowledgment keys of the warnings already acknowledged. */
  @Input() acknowledgedKeys: ReadonlySet<string> = new Set<string>();

  /** A check of the settings is pending; the counts are announced only once it settles. */
  @Input() busy = false;

  /** The prefix of every id the card renders; unique in the document. */
  @Input() idPrefix = 'mbReadiness';

  @Output() acknowledgedChange = new EventEmitter<ModelBatchAcknowledgment>();

  /** The finding's `field`, for the host to focus the control. */
  @Output() focusField = new EventEmitter<string>();

  blockers: BenchmarkModelBatchFindingDto[] = [];
  warnings: BenchmarkModelBatchFindingDto[] = [];
  advice: BenchmarkModelBatchFindingDto[] = [];

  /** The polite status line: the counts, updated once per settled check. */
  announcement = '';

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(): void {
    const findings = this.findings ?? [];
    this.blockers = findings.filter(f => f.severity === 'Blocker');
    this.warnings = findings.filter(f => f.severity === 'Warning');
    this.advice = findings.filter(f => f.severity === 'Advice');
    if (!this.busy) {
      this.announcement = this.countsText;
    }
  }

  /** Warnings with a key that is not acknowledged yet. */
  get unacknowledgedCount(): number {
    return this.warnings.filter(f => !this.isAcknowledged(f)).length;
  }

  get acknowledgedCount(): number {
    return this.warnings.filter(f => !!f.acknowledgmentKey && this.acknowledgedKeys.has(f.acknowledgmentKey)).length;
  }

  get state(): ModelBatchReadinessState {
    if (this.blockers.length > 0) return 'blocked';
    if (this.unacknowledgedCount > 0) return 'review';
    return 'ready';
  }

  /** *Ready*, *{n} to review* or *{n} blocking*. */
  get statusWord(): string {
    switch (this.state) {
      case 'blocked':
        return `${this.blockers.length} blocking`;
      case 'review':
        return `${this.unacknowledgedCount} to review`;
      default:
        return 'Ready';
    }
  }

  /** Something holds Start back, so the findings are shown. */
  get needsAttention(): boolean {
    return this.state !== 'ready';
  }

  /** The blockers, then the warnings: the lines above the tips. */
  get mainLines(): BenchmarkModelBatchFindingDto[] {
    return [...this.blockers, ...this.warnings];
  }

  get countsText(): string {
    const parts: string[] = [];
    if (this.blockers.length > 0) parts.push(`${this.blockers.length} blocking`);
    if (this.unacknowledgedCount > 0) parts.push(`${this.unacknowledgedCount} to review`);
    if (this.acknowledgedCount > 0) parts.push(`${this.acknowledgedCount} acknowledged`);
    if (this.advice.length > 0) parts.push(`${this.advice.length} ${this.advice.length === 1 ? 'tip' : 'tips'}`);
    const status = this.state === 'ready' ? 'Batch ready' : 'Batch not ready';
    return parts.length > 0 ? `${status}: ${parts.join(', ')}.` : `${status}.`;
  }

  isAcknowledged(finding: BenchmarkModelBatchFindingDto): boolean {
    // A warning without a key cannot be acknowledged, and the server does not hold Start for it.
    return !finding.acknowledgmentKey || this.acknowledgedKeys.has(finding.acknowledgmentKey);
  }

  onAcknowledgedChange(finding: BenchmarkModelBatchFindingDto, event: Event): void {
    if (!finding.acknowledgmentKey) return;
    this.acknowledgedChange.emit({
      key: finding.acknowledgmentKey,
      acknowledged: (event.target as HTMLInputElement).checked
    });
  }

  onGoToField(finding: BenchmarkModelBatchFindingDto): void {
    if (finding.field) {
      this.focusField.emit(finding.field);
    }
  }

  severityWord(finding: BenchmarkModelBatchFindingDto): string {
    switch (finding.severity) {
      case 'Blocker': return 'Blocking';
      case 'Warning':
        if (!finding.acknowledgmentKey) return 'Warning';
        return this.isAcknowledged(finding) ? 'Acknowledged' : 'To review';
      default: return 'Tip';
    }
  }

  severityKey(finding: BenchmarkModelBatchFindingDto): 'blocker' | 'warning' | 'advice' {
    switch (finding.severity) {
      case 'Blocker': return 'blocker';
      case 'Warning': return 'warning';
      default: return 'advice';
    }
  }

  /** The line's own id; its title, sentence and checkbox derive theirs from it. */
  itemId(finding: BenchmarkModelBatchFindingDto, index: number): string {
    return `${this.idPrefix}-${this.severityKey(finding)}-${index}`;
  }

  /** The info tip's id, from the code and the acknowledgment key (else the line's position). */
  tipId(finding: BenchmarkModelBatchFindingDto, index: number): string {
    const part = (value: string): string => value.replace(/[^A-Za-z0-9_-]+/g, '_');
    return `${this.idPrefix}-tip-${part(modelBatchFindingCode(finding))}-${part(finding.acknowledgmentKey || String(index))}`;
  }

  rationaleOf(finding: BenchmarkModelBatchFindingDto): string {
    if (finding.rationale) return finding.rationale;
    const own = MODEL_BATCH_FINDING_RATIONALE[modelBatchFindingCode(finding)];
    if (own) return own;
    switch (finding.severity) {
      case 'Blocker': return 'The server refuses the batch while this holds.';
      case 'Warning': return 'The batch can start once you acknowledge this; the acknowledgment is stored with the batch.';
      default: return 'A suggestion. It never holds Start back.';
    }
  }

  trackFinding(finding: BenchmarkModelBatchFindingDto, index: number): string {
    return `${modelBatchFindingCode(finding)}|${finding.acknowledgmentKey ?? ''}|${finding.field ?? ''}|${index}`;
  }
}
