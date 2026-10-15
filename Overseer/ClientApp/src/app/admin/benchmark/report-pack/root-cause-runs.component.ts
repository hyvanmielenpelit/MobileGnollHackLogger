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
import type { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  RecommendedRun,
  RecommendedRunPurpose,
  RootCauseCheck
} from '../../../services/admin-benchmark.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { formatCostUsd } from './report-document-format';

/** A recommended run's kind in words: *Replicate*, *Control run*, …; an unknown kind as sent. */
export function recommendedRunKindLabel(kind: string): string {
  switch (kind) {
    case 'replicate': return 'Replicate';
    case 'thinkingPair': return 'Thinking-level pair';
    case 'stylePair': return 'Response-style pair';
    case 'panelSwap': return 'Grader panel swap';
    case 'timeStratum': return 'Time-of-day repeat';
    case 'control': return 'Control run';
    default: return kind || 'Run';
  }
}

/** What a run is for, in words. */
export function recommendedRunPurposeLabel(purpose: RecommendedRunPurpose | string): string {
  switch (purpose) {
    case 'C': return 'For the chat';
    case 'B': return 'For the benchmark';
    case 'M': return 'For the models';
    default: return String(purpose ?? '');
  }
}

/** The note heading of a run a Root Cause Analysis recommends: *Set up from Root Cause Analysis #12, run 2*. */
export function rootCauseRunSourceLabel(documentId: number, runNumber: number): string {
  return `Set up from Root Cause Analysis #${documentId}, run ${runNumber}`;
}

/** `about $1.20 · about 35 min`; null when neither is known. */
export function recommendedRunEstimate(run: Pick<RecommendedRun, 'estimatedCostUsd' | 'estimatedMinutes'>): string | null {
  const parts: string[] = [];
  if (run.estimatedCostUsd !== null && run.estimatedCostUsd !== undefined && Number.isFinite(run.estimatedCostUsd)) {
    parts.push(`about ${formatCostUsd(run.estimatedCostUsd)}`);
  }
  if (run.estimatedMinutes !== null && run.estimatedMinutes !== undefined && Number.isFinite(run.estimatedMinutes)) {
    parts.push(`about ${Math.max(1, Math.round(run.estimatedMinutes))} min`);
  }
  return parts.length > 0 ? parts.join(' · ') : null;
}

/** One launcher setting of a recommended run, for its card. */
export interface RecommendedRunSetting {
  key: string;
  term: string;
  value: string;
}

/** The launcher settings a card summarizes, each only when the run sets it. */
export function recommendedRunSettings(run: RecommendedRun): RecommendedRunSetting[] {
  const settings: RecommendedRunSetting[] = [];
  const battery = run.targetKind === 'Battery';
  const batch = run.runMode === 'Batch';
  if (battery && (run.batteryName || run.batteryId != null)) {
    settings.push({ key: 'target', term: 'Battery', value: run.batteryName || `Battery #${run.batteryId}` });
  } else if (!battery && (run.suiteName || run.suiteId != null)) {
    settings.push({ key: 'target', term: 'Suite', value: run.suiteName || `Suite #${run.suiteId}` });
  }
  const names = run.modelNames?.length ? run.modelNames : (run.testedModelConfigurationIds ?? []).map(id => `configuration #${id}`);
  if (names.length > 0) {
    settings.push({ key: 'models', term: batch ? 'Model batch' : 'Model', value: names.join(', ') });
  }
  const runs = batch ? run.runsPerModel : run.runCount;
  if (runs !== null && runs !== undefined) {
    settings.push({
      key: 'runs',
      term: battery ? 'Runs per Suite' : (batch ? 'Runs per model' : 'Number of Runs'),
      value: String(runs)
    });
  }
  if (run.verboseMode !== null && run.verboseMode !== undefined) {
    settings.push({ key: 'style', term: 'Response style', value: run.verboseMode ? 'Detailed' : 'Concise' });
  }
  if (run.allowSourceCodeReferences !== null && run.allowSourceCodeReferences !== undefined) {
    settings.push({ key: 'sources', term: 'Source code references', value: run.allowSourceCodeReferences ? 'Allowed' : 'Not allowed' });
  }
  return settings;
}

/** A recommended run as its card shows it. */
interface RootCauseRunCard {
  key: string;
  /** 1-based, in the document's order. */
  number: number;
  titleId: string;
  title: string;
  purpose: string;
  mark: string;
  hypothesisIds: readonly string[];
  decides: string;
  predictedTier: string;
  estimate: string;
  settings: RecommendedRunSetting[];
  /** A model-selection run to make only after the system hypotheses are settled. */
  afterSystemHypotheses: boolean;
  notes: readonly string[];
  run: RecommendedRun;
}

/**
 * A Root Cause Analysis' next runs and checks, in a disclosure under its document row: one card per
 * recommended run (its kind, purpose and mark, the decision rule stated before the run, the predicted
 * comparability tier, the estimate and a settings summary) with **Set up this run**, which fills Run
 * Benchmark through the shell bridge and starts nothing, then the checks no run can settle. Given no
 * runs and checks, it reads them from the document's detail the first time it is opened.
 */
@Component({
  selector: 'app-root-cause-runs',
  standalone: true,
  templateUrl: './root-cause-runs.component.html',
  styleUrls: ['./root-cause-runs.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RootCauseRunsComponent implements OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly bridge = inject(BenchmarkShellBridge, { optional: true });

  /** The Root Cause Analysis whose runs these are. */
  @Input({ required: true }) documentId!: number;
  /** Its recommended runs; undefined reads them from the detail on the first opening. */
  @Input() recommendedRuns: readonly RecommendedRun[] | null | undefined = undefined;
  /** Its checks; undefined reads them with the runs. */
  @Input() checks: readonly RootCauseCheck[] | null | undefined = undefined;
  /** The disclosure starts open. */
  @Input() open = false;

  runs: readonly RecommendedRun[] = [];
  checkList: readonly RootCauseCheck[] = [];
  cards: RootCauseRunCard[] = [];
  /** The runs and checks are in hand. */
  loaded = false;
  loading = false;
  loadError: string | null = null;
  /** Why the last **Set up this run** was refused, by card key. */
  refusals = new Map<string, string>();

  private loadSub: Subscription | null = null;

  get idPrefix(): string {
    return `rcr-${this.documentId}`;
  }

  get summaryText(): string {
    if (!this.loaded) {
      return 'Next runs and checks';
    }
    return `Next runs and checks (${this.runs.length} ${this.runs.length === 1 ? 'run' : 'runs'}, `
      + `${this.checkList.length} ${this.checkList.length === 1 ? 'check' : 'checks'})`;
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['documentId'] && !changes['documentId'].firstChange) {
      this.loadSub?.unsubscribe();
      this.loadSub = null;
      this.loaded = false;
      this.loading = false;
      this.loadError = null;
      this.refusals = new Map();
    }
    if (this.recommendedRuns !== undefined || this.checks !== undefined) {
      this.setData(this.recommendedRuns ?? [], this.checks ?? []);
    } else if (this.open && !this.loaded) {
      this.load();
    }
  }

  ngOnDestroy(): void {
    this.loadSub?.unsubscribe();
    this.loadSub = null;
  }

  /** The disclosure opened: the detail is read once. */
  onToggle(event: Event): void {
    if ((event.target as HTMLDetailsElement).open && !this.loaded && !this.loading) {
      this.load();
    }
  }

  /** Reads the document's detail again after a failure. */
  load(): void {
    const documentId = this.documentId;
    this.loading = true;
    this.loadError = null;
    this.loadSub?.unsubscribe();
    this.loadSub = this.benchmarkService.getReportDocument(documentId).subscribe({
      next: detail => {
        if (documentId !== this.documentId) return;
        this.loading = false;
        this.setData(detail?.recommendedRuns ?? [], detail?.checks ?? []);
        this.cdr.markForCheck();
      },
      error: () => {
        if (documentId !== this.documentId) return;
        this.loading = false;
        this.loadError = 'The recommended runs could not be read.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  private setData(runs: readonly RecommendedRun[], checks: readonly RootCauseCheck[]): void {
    this.runs = runs;
    this.checkList = checks.map(check => ({ ...check, hypothesisIds: check.hypothesisIds ?? [] }));
    this.cards = runs.map((run, index) => {
      const number = run.number > 0 ? run.number : index + 1;
      return {
        key: `${index}-${run.kind}`,
        number,
        titleId: `${this.idPrefix}-run-${number}`,
        title: `Run ${number}: ${recommendedRunKindLabel(run.kind)}`,
        purpose: recommendedRunPurposeLabel(run.purpose),
        mark: run.mark || 'Recommended',
        hypothesisIds: run.hypothesisIds ?? [],
        decides: run.decides,
        predictedTier: run.predictedTier || 'Not predicted',
        estimate: recommendedRunEstimate(run) ?? 'Not estimated',
        settings: recommendedRunSettings(run),
        afterSystemHypotheses: run.afterSystemHypotheses === true,
        // The card states the wait itself, so the server's note saying the same is not repeated.
        notes: (run.notes ?? []).filter(note => !(run.afterSystemHypotheses && note.includes('after the system hypotheses are settled'))),
        run
      };
    });
    this.loaded = true;
  }

  /**
   * **Set up this run**: Run Benchmark opens with the run's settings, under the heading *Set up from Root
   * Cause Analysis #<id>, run <k>*; nothing starts. Refused, with the reason on the card, while the
   * active sub-tab cannot be left or outside the GnollBench tab.
   */
  setUp(card: RootCauseRunCard): void {
    const refusal = this.bridge
      ? this.bridge.leaveRefusal()
      : 'Open this document from the GnollBench tab to set up the run.';
    if (refusal) {
      this.refusals = new Map(this.refusals).set(card.key, refusal);
      this.cdr.markForCheck();
      return;
    }
    this.refusals = new Map();
    this.bridge!.prefillSetup(card.run, rootCauseRunSourceLabel(this.documentId, card.number));
  }
}
