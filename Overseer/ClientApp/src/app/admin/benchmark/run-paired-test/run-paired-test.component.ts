import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkPairComparisonDto,
  BenchmarkPairKind,
  BenchmarkReportPackPricingBasis,
  BenchmarkRunDetailDto,
  BenchmarkRunPairKindDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { formatStatus, isAbortedRun } from '../benchmark-run-format';
import { kindDescription, pairedErrorText } from '../shared/paired-test/paired-test-format';
import { PairedTestResultComponent } from '../shared/paired-test/paired-test-result.component';

/** The kinds a baseline can make with this run, in the order the select groups them. */
const KIND_ORDER: readonly BenchmarkPairKind[] = ['ModelComparison', 'Verification', 'Replicate'];

const KIND_GROUP_LABELS: Readonly<Record<BenchmarkPairKind, string>> = {
  ModelComparison: 'Model comparison',
  Verification: 'Verification of a change',
  Replicate: 'Replicate'
};

/** One baseline the select offers. */
export interface RunPairedOption {
  readonly run: BenchmarkRunSummaryDto;
  readonly label: string;
  readonly kind: BenchmarkRunPairKindDto | null;
}

/** One optgroup of the select. */
export interface RunPairedGroup {
  readonly label: string;
  readonly options: readonly RunPairedOption[];
}

/** A run that finished with a measurement: the server refuses running, failed and canceled runs. */
export function isFinishedRun(run: BenchmarkRunSummaryDto): boolean {
  const status = formatStatus(run.status);
  return (status === 'Completed' || status === 'CompletedWithErrors' || status === 'CompletedWithLimits')
    && !isAbortedRun(run);
}

/** `#12 · Gemini 3 Flash (high) · 71.2 · 2026-09-30`. */
export function runPairedOptionLabel(run: BenchmarkRunSummaryDto): string {
  const thinking = run.testedModelThinkingLevelUsed ? ` (${run.testedModelThinkingLevelUsed})` : '';
  const index = run.qualityIndex != null && Number.isFinite(run.qualityIndex) ? run.qualityIndex.toFixed(1) : 'no index';
  const date = parseServerUtcDate(run.startedAtUtc);
  const day = Number.isNaN(date.getTime()) ? '' : ` · ${date.toISOString().slice(0, 10)}`;
  return `#${run.id} · ${run.testedModelDisplayNameUsed || run.testedModelIdUsed}${thinking} · ${index}${day}`;
}

/**
 * The run report's **Paired Test** tab: this run (the treatment) against another finished run on the
 * same suite, on every measure — the Intelligence Index first, then the quality dimensions, speed and
 * cost — as one unadjusted pair. The baselines are grouped by the kind of comparison they would make
 * (model comparison, verification of a change, replicate), which the server decides from the
 * comparability keys; a run that is not comparable is left out and counted.
 *
 * The kinds are fetched when the tab is first shown for a run; Compare fetches the result.
 */
@Component({
  selector: 'app-run-paired-test',
  standalone: true,
  imports: [PairedTestResultComponent],
  templateUrl: './run-paired-test.component.html',
  styleUrls: ['./run-paired-test.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RunPairedTestComponent implements OnChanges, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly benchmarkService = inject(AdminBenchmarkService);

  /** The run whose report this is: the treatment. */
  @Input() run: BenchmarkRunDetailDto | null = null;

  /** The runs the baseline is chosen from: the workspace store's `historyRuns`, in any order. */
  @Input() candidateRuns: readonly BenchmarkRunSummaryDto[] = [];

  /** The tab is shown; the kinds are fetched only once it is. */
  @Input() active = false;

  /** The price list the cost row is computed on. */
  @Input() pricingBasis: BenchmarkReportPackPricingBasis = BenchmarkReportPackPricingBasis.Current;

  /** Finished runs on the same suite, this one excluded, newest first. */
  candidates: readonly BenchmarkRunSummaryDto[] = [];

  /** By run id, once the server has classified the candidates. */
  kinds: ReadonlyMap<number, BenchmarkRunPairKindDto> | null = null;
  kindsLoading = false;
  kindsError: string | null = null;

  groups: readonly RunPairedGroup[] = [];
  notComparableCount = 0;

  baselineId: number | null = null;
  comparing = false;
  result: BenchmarkPairComparisonDto | null = null;
  error: string | null = null;

  /** The run and candidate ids the kinds were requested for. */
  private kindsSignature: string | null = null;
  private kindsSub: Subscription | null = null;
  private compareSub: Subscription | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    const runChange = changes['run'];
    if (runChange && (runChange.previousValue as BenchmarkRunDetailDto | null)?.id !== this.run?.id) {
      this.resetForRun();
    }
    if (runChange || changes['candidateRuns']) {
      this.candidates = this.buildCandidates();
      this.buildGroups();
    }
    this.ensureKinds();
  }

  ngOnDestroy(): void {
    this.kindsSub?.unsubscribe();
    this.compareSub?.unsubscribe();
  }

  private resetForRun(): void {
    this.kindsSub?.unsubscribe();
    this.compareSub?.unsubscribe();
    this.kinds = null;
    this.kindsLoading = false;
    this.kindsError = null;
    this.kindsSignature = null;
    this.baselineId = null;
    this.comparing = false;
    this.result = null;
    this.error = null;
  }

  private buildCandidates(): BenchmarkRunSummaryDto[] {
    const run = this.run;
    if (!run || run.benchmarkSuiteId == null) {
      return [];
    }
    return this.candidateRuns
      .filter(candidate => candidate.id !== run.id
        && candidate.benchmarkSuiteId === run.benchmarkSuiteId
        && isFinishedRun(candidate))
      .slice()
      .sort((a, b) => parseServerUtcDate(b.startedAtUtc).getTime() - parseServerUtcDate(a.startedAtUtc).getTime()
        || b.id - a.id);
  }

  /** Asks the server which kind each candidate would make, once per run and candidate set, while the tab is shown. */
  private ensureKinds(): void {
    const run = this.run;
    if (!this.active || !run || this.candidates.length === 0) {
      return;
    }
    const ids = this.candidates.map(candidate => candidate.id);
    const signature = `${run.id}:${ids.join(',')}`;
    if (signature === this.kindsSignature) {
      return;
    }
    this.kindsSignature = signature;
    this.kindsSub?.unsubscribe();
    this.kindsLoading = true;
    this.kindsError = null;
    this.cdr.markForCheck();
    this.kindsSub = this.benchmarkService.getRunPairKinds(run.id, ids).subscribe({
      next: kinds => {
        this.kinds = new Map(kinds.map(kind => [kind.runId, kind]));
        this.kindsLoading = false;
        this.buildGroups();
        this.cdr.markForCheck();
      },
      error: (err: unknown) => {
        this.kindsLoading = false;
        this.kindsError = pairedErrorText(err, 'The runs could not be classified; every finished run on this suite is listed.');
        this.buildGroups();
        this.cdr.markForCheck();
      }
    });
  }

  /** The select's groups: one per kind, in a fixed order; ungrouped until the kinds are known. */
  private buildGroups(): void {
    const kinds = this.kinds;
    const options = this.candidates.map((run): RunPairedOption => ({
      run, label: runPairedOptionLabel(run), kind: kinds?.get(run.id) ?? null
    }));
    if (!kinds) {
      this.groups = options.length > 0 ? [{ label: 'Finished runs on this suite', options }] : [];
      this.notComparableCount = 0;
      return;
    }
    this.groups = KIND_ORDER
      .map(kind => ({ label: KIND_GROUP_LABELS[kind], options: options.filter(option => option.kind?.kind === kind) }))
      .filter(group => group.options.length > 0);
    this.notComparableCount = options.filter(option => !option.kind || option.kind.kind === 'NotComparable').length;
    if (this.baselineId !== null && !this.groups.some(group => group.options.some(option => option.run.id === this.baselineId))) {
      this.baselineId = null;
    }
  }

  /** The chosen baseline's option. */
  get selectedOption(): RunPairedOption | null {
    for (const group of this.groups) {
      const option = group.options.find(candidate => candidate.run.id === this.baselineId);
      if (option) {
        return option;
      }
    }
    return null;
  }

  /** The kind sentence for the chosen baseline: the server's label and explanation. */
  get kindText(): string {
    const kind = this.selectedOption?.kind;
    if (!kind) {
      return this.baselineId === null ? 'Choose a baseline.' : '';
    }
    return kindDescription(kind.kind, kind.kindLabel, kind.explanation);
  }

  /** A verification's changed keys, named under the kind sentence. */
  get changedKeys(): readonly string[] {
    const kind = this.selectedOption?.kind;
    return kind?.kind === 'Verification' ? kind.changedKeys : [];
  }

  get canCompare(): boolean {
    return !this.comparing && this.run !== null && this.selectedOption !== null;
  }

  onBaselineChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.baselineId = value === '' ? null : Number(value);
    this.compareSub?.unsubscribe();
    this.comparing = false;
    this.result = null;
    this.error = null;
    this.cdr.markForCheck();
  }

  compare(): void {
    const run = this.run;
    const baselineId = this.baselineId;
    if (!this.canCompare || !run || baselineId === null) {
      return;
    }
    this.compareSub?.unsubscribe();
    this.comparing = true;
    this.result = null;
    this.error = null;
    this.cdr.markForCheck();
    this.compareSub = this.benchmarkService.getRunPairedComparison(run.id, baselineId, this.pricingBasis).subscribe({
      next: result => {
        this.comparing = false;
        this.result = result;
        this.cdr.markForCheck();
      },
      error: (err: unknown) => {
        this.comparing = false;
        this.error = pairedErrorText(err, 'The two runs could not be compared.');
        this.cdr.markForCheck();
      }
    });
  }

  /** `#12 · Gemini 3 Flash · 71.2` for this run, beside the select. */
  get treatmentLabel(): string {
    const run = this.run;
    if (!run) {
      return '';
    }
    const thinking = run.testedModelThinkingLevelUsed ? ` (${run.testedModelThinkingLevelUsed})` : '';
    const index = run.qualityIndex != null && Number.isFinite(run.qualityIndex) ? ` · ${run.qualityIndex.toFixed(1)}` : '';
    return `#${run.id} · ${run.testedModelDisplayNameUsed || run.testedModelIdUsed}${thinking}${index}`;
  }
}
