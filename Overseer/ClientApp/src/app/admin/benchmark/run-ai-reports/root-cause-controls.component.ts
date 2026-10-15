import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';
import { Observable, Subscription, catchError, forkJoin, map, of } from 'rxjs';

import {
  AdminBenchmarkService,
  RootCauseControlDto,
  RootCauseControlsDto
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';

/** Whose controls: a run's or a battery run's. */
export type RootCauseControlsSubjectKind = 'run' | 'battery';

/** What **Rewrite with controls** hands to the host: the stored Root Cause Analysis and the controls now available. */
export interface RootCauseRewriteRequest {
  documentId: number;
  controls: RootCauseControlDto[];
}

/** The sentence with no qualifying control. */
export const ROOT_CAUSE_NO_CONTROLS_TEXT = 'No earlier run of another model answered these questions under the same graders. ' +
  'Speed and cost causes will be analyzed; intelligence causes will stay undetermined.';

/** The note pointing several models to the comparison's documents. */
export const ROOT_CAUSE_SEVERAL_MODELS_NOTE = 'For several models, Model Comparison → Report Pack gives a fuller analysis.';

/** `2026-10-10`, or '' for a date that cannot be read. */
function dayOf(iso: string | null | undefined): string {
  if (!iso) return '';
  const date = parseServerUtcDate(iso);
  return Number.isNaN(date.getTime()) ? '' : date.toISOString().slice(0, 10);
}

/** `run #812`, or `battery run #31`. */
export function rootCauseControlRef(control: RootCauseControlDto): string {
  if (control.batteryRunId !== null && control.batteryRunId !== undefined) {
    return `battery run #${control.batteryRunId}`;
  }
  return control.runId !== null && control.runId !== undefined ? `run #${control.runId}` : 'an earlier run';
}

/** `GPT-6 Luna (high), run #812 (2026-10-10)`. */
export function rootCauseControlPhrase(control: RootCauseControlDto): string {
  const day = dayOf(control.completedAtUtc);
  return `${control.modelLabel}, ${rootCauseControlRef(control)}${day ? ` (${day})` : ''}`;
}

/** `2 controls: A, run #812 (2026-10-10); B, run #807 (2026-10-09)`. */
function controlsList(controls: readonly RootCauseControlDto[]): string {
  const count = `${controls.length} ${controls.length === 1 ? 'control' : 'controls'}`;
  return `${count}: ${controls.map(rootCauseControlPhrase).join('; ')}`;
}

/** *The Root Cause Analysis will use 2 controls: …*; the none-found sentence with none. */
export function rootCauseControlsLine(controls: readonly RootCauseControlDto[]): string {
  return controls.length === 0 ? ROOT_CAUSE_NO_CONTROLS_TEXT : `The Root Cause Analysis will use ${controlsList(controls)}.`;
}

/** *This Root Cause Analysis used 2 controls: …*, or that it used none. */
export function rootCauseUsedControlsLine(controls: readonly RootCauseControlDto[]): string {
  return controls.length === 0
    ? 'This Root Cause Analysis used no control: no earlier run of another model had answered these questions under the same graders.'
    : `This Root Cause Analysis used ${controlsList(controls)}.`;
}

/** The confidence cap a control limit puts on the intelligence causes, or '' without one. */
export function rootCauseControlLimitText(limit: RootCauseControlsDto['controlLimit'] | null | undefined): string {
  switch (limit) {
    case 'Moderate': return 'With these controls, an intelligence cause can be graded at most Moderate.';
    case 'Low': return 'Without a qualifying control, an intelligence cause can be graded at most Low.';
    default: return '';
  }
}

/** The key that identifies a control across the stored document and a fresh lookup. */
function controlKey(control: RootCauseControlDto): string {
  return control.batteryRunId !== null && control.batteryRunId !== undefined
    ? `battery:${control.batteryRunId}`
    : `run:${control.runId ?? ''}`;
}

/** The fresh controls the stored document did not use. */
export function newRootCauseControls(
  stored: readonly RootCauseControlDto[],
  fresh: readonly RootCauseControlDto[]
): RootCauseControlDto[] {
  const used = new Set(stored.map(controlKey));
  return fresh.filter(control => !used.has(controlKey(control)));
}

/** What the component read: the controls now, and the stored document's, when there is one. */
interface ControlsLoad {
  fresh: RootCauseControlsDto | null;
  stored: RootCauseControlDto[] | null;
  failed: boolean;
}

/**
 * The run and battery AI Reports tabs' **Controls** line for the Root Cause Analysis. Before the
 * document is written: which earlier runs of other models it will read as controls, or that there are
 * none, with **Set up a control run**, which fills Run Benchmark with the server's control run and starts
 * nothing. After: when more controls qualify now than the stored document used, *New controls available
 * since this document was written* with **Rewrite with controls**, which the host confirms and writes.
 */
@Component({
  selector: 'app-root-cause-controls',
  standalone: true,
  templateUrl: './root-cause-controls.component.html',
  styleUrls: ['./root-cause-controls.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RootCauseControlsComponent implements OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly bridge = inject(BenchmarkShellBridge, { optional: true });

  @Input({ required: true }) subjectKind!: RootCauseControlsSubjectKind;
  @Input({ required: true }) subjectId: number | null = null;
  /** The stored Root Cause Analysis, or null while it is not written. */
  @Input() rcaDocumentId: number | null = null;
  /** A job writes the subject's documents: no rewrite now. */
  @Input() busy = false;
  /** Bumped by the host to read the controls again. */
  @Input() reloadToken = 0;
  /** The prefix of the element ids. */
  @Input() idPrefix = 'rcc';

  /** **Rewrite with controls**: the host confirms and writes. */
  @Output() readonly rewriteRequested = new EventEmitter<RootCauseRewriteRequest>();

  fresh: RootCauseControlsDto | null = null;
  /** The controls the stored document used; null while not read or without a document. */
  stored: RootCauseControlDto[] | null = null;
  loading = false;
  loadError: string | null = null;
  /** Why the last **Set up a control run** was refused. */
  setUpRefusal: string | null = null;

  readonly severalModelsNote = ROOT_CAUSE_SEVERAL_MODELS_NOTE;
  readonly busyReason = 'Wait for the writing to finish, or cancel it.';

  private loadSub: Subscription | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['subjectKind'] || changes['subjectId'] || changes['rcaDocumentId'] || changes['reloadToken']) {
      this.load();
    }
  }

  ngOnDestroy(): void {
    this.loadSub?.unsubscribe();
    this.loadSub = null;
  }

  get controls(): RootCauseControlDto[] {
    return this.fresh?.controls ?? [];
  }

  /** The document is not written yet, so the line says what the analysis will use. */
  get beforeWriting(): boolean {
    return this.rcaDocumentId === null;
  }

  get controlsLine(): string {
    return rootCauseControlsLine(this.controls);
  }

  get usedLine(): string {
    return rootCauseUsedControlsLine(this.stored ?? []);
  }

  get limitText(): string {
    return this.controls.length > 0 ? rootCauseControlLimitText(this.fresh?.controlLimit) : '';
  }

  /** **Set up a control run** is shown: with no control, and with one when the server proposes a run. */
  get offersControlRun(): boolean {
    return !!this.fresh && (this.controls.length === 0 || (this.controls.length < 2 && this.fresh.controlRun !== null));
  }

  /** Why **Set up a control run** is unavailable, or ''. */
  get controlRunUnavailable(): string {
    return this.fresh?.controlRun
      ? ''
      : 'No control run can be proposed: no other provider has a recommended model with an enabled Benchmark configuration.';
  }

  /** The controls now that the stored document did not use, when more qualify now than it used. */
  get newControls(): RootCauseControlDto[] {
    if (this.stored === null || !this.fresh || this.controls.length <= this.stored.length) {
      return [];
    }
    return newRootCauseControls(this.stored, this.controls);
  }

  /** `run #812, run #815`. */
  get newControlsText(): string {
    return this.newControls.map(rootCauseControlRef).join(', ');
  }

  get subjectRef(): string {
    return this.subjectKind === 'battery' ? `battery run #${this.subjectId}` : `run #${this.subjectId}`;
  }

  /** Reads the controls now and, with a stored document, the controls it used. */
  load(): void {
    const kind = this.subjectKind;
    const subjectId = this.subjectId;
    const documentId = this.rcaDocumentId;
    if (subjectId === null || subjectId === undefined || !kind) return;
    this.loading = true;
    this.loadError = null;
    this.setUpRefusal = null;
    this.loadSub?.unsubscribe();
    const fresh$: Observable<RootCauseControlsDto> = kind === 'battery'
      ? this.benchmarkService.getBatteryRootCauseControls(subjectId)
      : this.benchmarkService.getRunRootCauseControls(subjectId);
    const stored$: Observable<RootCauseControlDto[] | null> = documentId === null
      ? of(null)
      : this.benchmarkService.getReportDocument(documentId).pipe(map(detail => detail?.controls ?? []));
    this.loadSub = forkJoin({ fresh: fresh$, stored: stored$ }).pipe(
      map((result): ControlsLoad => ({ fresh: result.fresh, stored: result.stored, failed: false })),
      catchError(() => of<ControlsLoad>({ fresh: null, stored: null, failed: true }))
    ).subscribe(result => {
      if (kind !== this.subjectKind || subjectId !== this.subjectId || documentId !== this.rcaDocumentId) return;
      this.loading = false;
      this.fresh = result.fresh;
      this.stored = result.stored;
      this.loadError = result.failed ? 'The controls of the Root Cause Analysis could not be looked up.' : null;
      this.cdr.markForCheck();
    });
    this.cdr.markForCheck();
  }

  /**
   * **Set up a control run**: Run Benchmark opens with the server's control run, under the heading
   * *Control run for run #<id>*; nothing starts. Refused, with the reason under the button, while the
   * active sub-tab cannot be left.
   */
  setUpControlRun(): void {
    const run = this.fresh?.controlRun;
    if (!run) return;
    const refusal = this.bridge
      ? this.bridge.leaveRefusal()
      : 'Open this report from the GnollBench tab to set up the run.';
    if (refusal) {
      this.setUpRefusal = refusal;
      this.cdr.markForCheck();
      return;
    }
    this.setUpRefusal = null;
    this.bridge!.prefillSetup(run, `Control run for ${this.subjectRef}`);
  }

  /** **Rewrite with controls**: handed to the host, which confirms the estimate and writes. */
  requestRewrite(): void {
    const documentId = this.rcaDocumentId;
    if (documentId === null || this.busy || this.newControls.length === 0) return;
    this.rewriteRequested.emit({ documentId, controls: [...this.controls] });
  }
}
