import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, forkJoin } from 'rxjs';

import { AdminChatConsistencyService, ccErrorText } from '../../../services/admin-chat-consistency.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { CC_WIZARD_STEPS, CcWizardComponent, CcWizardStep } from './cc-wizard/cc-wizard.component';
import { utcMillis } from './chat-consistency-format';
import {
  CC_ALL_DATES,
  CC_RANGE_PRESETS,
  CcDateRange,
  ccAnchorRange,
  ccDateRangeText,
  ccRangeBounds
} from './chat-consistency-range';
import {
  CC_EMPTY_SCOPE,
  CcRunScope,
  CcScopeUnit,
  ccEmptyScope,
  comparisonSetUnits,
  pruneScope,
  scopeIsDefault,
  scopeRuns,
  setUnitKind
} from './chat-consistency-scope';
import {
  CcAnalysisResult,
  CcAnalysisSummary,
  CcBatteryRunRow,
  CcComparisonSet,
  CcComparisonSetRef,
  CcComparisonSets,
  CcModelAxis,
  CcOpenDocumentsRequest,
  CcRunRow,
  CcTimeline,
  CcUnitKind
} from './chat-consistency.models';
import { CcCurrentModelCardComponent } from './current-model-card/current-model-card.component';
import { CcSavedAnalysesComponent } from './saved-analyses/saved-analyses.component';

/** Where the launcher's *How chat consistency works* state is kept, per browser. */
export const CC_LAUNCHER_STORAGE_KEY = 'overseer.benchmark.chatConsistency.launcher';

/** The version of the stored launcher record; a record of another version reads as none. */
const CC_LAUNCHER_STORAGE_VERSION = 1;

/** Where the current model, its dates and the compared set are kept, per browser, so a reload keeps them. */
export const CC_SUBJECT_STORAGE_KEY = 'overseer.benchmark.chatConsistency.subject';

/**
 * The version of the stored subject record. Version 1, which had no compared set, is still read as a
 * record without one; a record of any other version reads as none.
 */
const CC_SUBJECT_STORAGE_VERSION = 2;

/** The stored subject: the model's axis key, the date range chosen for it and the compared set. */
interface CcStoredSubject {
  version: number;
  modelKey: string;
  range: CcDateRange;
  compare: CcComparisonSetRef | null;
}

/** The announcement when another compared set clears a step-1 selection. */
export const CC_SET_CHANGE_NOTE = 'The selection in step 1 was cleared because another battery or suite is compared.';

/** The run report's refusal to switch sub-tabs while the wizard is blocked. */
export const CC_LEAVE_REFUSAL =
  'Chat Consistency is exporting charts or attaching report charts. Wait for it to finish, then try again.';

/**
 * The Chat Consistency sub-tab: whether the Overseer chat with one model stayed the same over time.
 * A launcher page — what the view does, the current model, how it works and the saved analyses —
 * opens the four-step wizard in a full-screen dialog.
 *
 * It owns the subject, the date range and their data, and performs the run actions the wizard asks
 * for: anchors through the API, *Repeat this run's setup* and *Open run report* through the shell.
 */
@Component({
  selector: 'app-chat-consistency-tab',
  standalone: true,
  imports: [CcWizardComponent, CcCurrentModelCardComponent, CcSavedAnalysesComponent],
  templateUrl: './chat-consistency-tab.component.html',
  styleUrls: ['./chat-consistency-tab.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChatConsistencyTabComponent implements OnInit, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly bridge = inject(BenchmarkShellBridge);
  private readonly cdr = inject(ChangeDetectorRef);
  readonly workspace = inject(BenchmarkWorkspaceStore);

  /** The Download Center on an analysis's documents. */
  @Output() readonly openDocuments = new EventEmitter<CcOpenDocumentsRequest>();

  @ViewChild('wizardDialog') wizardDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild(CcWizardComponent) wizard?: CcWizardComponent;

  /** The wizard's steps, which the launcher lists under the same titles as the wizard's step tabs. */
  readonly wizardSteps = CC_WIZARD_STEPS;

  /**
   * The wizard's content exists: set on the first opening and never reset, so reopening keeps the
   * step, the tables' sort and page, the charts and an analysis in progress.
   */
  wizardMounted = false;

  /** Whether *How chat consistency works* is open; null until the stored state is read. */
  howItWorksOpen: boolean | null = null;

  axes: CcModelAxis[] = [];
  axesLoading = false;
  axesError: string | null = null;

  selectedKey: string | null = null;
  range: CcDateRange = CC_ALL_DATES;
  /**
   * The compared battery or suite. Before the sets arrive it is the preferred one (the stored choice),
   * which the load keeps while it is offered and otherwise replaces with the server's default.
   */
  compareKey: string | null = null;
  /** The units of step 1 the analysis uses: the first and last unit marks and the units left out. */
  scope: CcRunScope = CC_EMPTY_SCOPE;
  timeline: CcTimeline | null = null;
  rows: CcRunRow[] = [];
  comparisonSets: CcComparisonSets | null = null;
  batteryRows: CcBatteryRunRow[] = [];
  subjectLoading = false;
  timelineError: string | null = null;
  runsError: string | null = null;

  anchorBusy = new Set<number>();
  anchorError: string | null = null;
  announcement = '';

  analyses: CcAnalysisSummary[] = [];
  analysesLoading = false;
  analysesError: string | null = null;
  openingId: number | null = null;
  openError: string | null = null;

  private axesSub: Subscription | null = null;
  private subjectSub: Subscription | null = null;
  private analysesSub: Subscription | null = null;
  private openSub: Subscription | null = null;
  private readonly anchorSubs = new Map<number, Subscription>();
  private releaseLeaveGuard: (() => void) | null = null;
  private destroyed = false;
  /** The element focused when a refused Escape arrived, restored if the dialog has to be reopened. */
  private focusBeforeCancel: HTMLElement | null = null;

  constructor() {
    // The configurations the pickers offer arrive through the shell; OnPush needs telling.
    inject(BenchmarkViewSync).changed$.pipe(takeUntilDestroyed()).subscribe(() => this.cdr.markForCheck());
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.restoreHowItWorks();
    this.loadAxes();
    this.loadAnalyses();
    // The run report's actions that switch to Run Benchmark would destroy this tab, and the wizard with it.
    this.releaseLeaveGuard = this.bridge.setLeaveGuard(() => this.wizard?.closeBlocked ? CC_LEAVE_REFUSAL : null);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.releaseLeaveGuard?.();
    this.releaseLeaveGuard = null;
    // A sub-tab switch must not leave a modal behind.
    const dialog = this.wizardDialog?.nativeElement;
    if (dialog?.open) dialog.close();
    for (const sub of [this.axesSub, this.subjectSub, this.analysesSub, this.openSub, ...this.anchorSubs.values()]) {
      sub?.unsubscribe();
    }
  }

  get selectedAxis(): CcModelAxis | null {
    return this.axes.find(axis => axis.key === this.selectedKey) ?? null;
  }

  /** The compared set, once the sets are loaded and offer it. */
  get compareSet(): CcComparisonSet | null {
    return this.comparisonSets?.sets.find(set => set.key === this.compareKey) ?? null;
  }

  /** What step 1's selection counts: battery runs in a battery set, runs otherwise. */
  get unitKind(): CcUnitKind {
    return setUnitKind(this.compareSet?.key);
  }

  /** The units of the compared set; every run while no set is compared. */
  private units(): readonly CcScopeUnit[] {
    return comparisonSetUnits(this.rows, this.batteryRows, this.compareSet?.key);
  }

  // --- The Current model card ---

  get currentDatesText(): string {
    return ccDateRangeText(this.range);
  }

  /** The runs in the chosen dates; null over every date, or before the timeline is loaded. */
  get runsInRange(): number | null {
    return this.range.preset !== 'all' && this.timeline ? this.timeline.points.length : null;
  }

  /** The units the analysis uses; null when step 1 does not narrow them. */
  get runsInAnalysis(): number | null {
    return scopeIsDefault(this.scope) ? null : scopeRuns(this.units(), this.scope).length;
  }

  /** The newest saved analysis of the current model. */
  get latestAnalysis(): CcAnalysisSummary | null {
    const key = this.selectedKey;
    if (!key) return null;
    let latest: CcAnalysisSummary | null = null;
    for (const analysis of this.analyses) {
      if (analysis.subjectModelKey !== key) continue;
      if (!latest || utcMillis(analysis.createdAtUtc) > utcMillis(latest.createdAtUtc)) latest = analysis;
    }
    return latest;
  }

  // --- How chat consistency works ---

  /**
   * Reads the disclosure state once. With no stored record it opens, and records it closed, so only
   * the first visit shows it open unless the operator leaves it that way.
   */
  private restoreHowItWorks(): void {
    let stored: string | null;
    try {
      stored = localStorage.getItem(CC_LAUNCHER_STORAGE_KEY);
    } catch {
      this.howItWorksOpen = true;
      return;
    }
    let record: { version?: unknown; howItWorksOpen?: unknown } | null = null;
    if (stored !== null) {
      try {
        record = JSON.parse(stored) as { version?: unknown; howItWorksOpen?: unknown } | null;
      } catch {
        record = null;
      }
    }
    if (!record || record.version !== CC_LAUNCHER_STORAGE_VERSION) {
      this.howItWorksOpen = true;
      this.persistHowItWorks(false);
      return;
    }
    this.howItWorksOpen = record.howItWorksOpen === true;
  }

  /**
   * The disclosure's native toggle. Setting [open] from the binding fires it too, so a state that
   * matches the field is the binding's own echo and is not stored.
   */
  onHowItWorksToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.howItWorksOpen) return;
    this.howItWorksOpen = open;
    this.persistHowItWorks(open);
  }

  private persistHowItWorks(open: boolean): void {
    try {
      localStorage.setItem(CC_LAUNCHER_STORAGE_KEY, JSON.stringify({ version: CC_LAUNCHER_STORAGE_VERSION, howItWorksOpen: open }));
    } catch {
      // Storage throws in private-browsing modes; a forgotten disclosure state is not worth reporting.
    }
  }

  // --- The remembered subject ---

  /** Records the current model, its dates and the compared set; nothing while no model is chosen. */
  private persistSubject(): void {
    const modelKey = this.selectedKey;
    if (!modelKey) return;
    const key = this.compareKey;
    const record: CcStoredSubject = {
      version: CC_SUBJECT_STORAGE_VERSION,
      modelKey,
      range: this.range,
      compare: key ? { kind: setUnitKind(key) === 'batteryRun' ? 'battery' : 'suite', key } : null
    };
    try {
      localStorage.setItem(CC_SUBJECT_STORAGE_KEY, JSON.stringify(record));
    } catch {
      // Storage throws in private-browsing modes; the wizard then starts without a model.
    }
  }

  /** A stored compared set, or null when it is absent or not a set reference. */
  private static readStoredCompare(value: unknown): CcComparisonSetRef | null {
    if (!value || typeof value !== 'object') return null;
    const raw = value as { kind?: unknown; key?: unknown };
    const kind = raw.kind === 'battery' ? 'battery' : raw.kind === 'suite' ? 'suite' : null;
    const key = raw.key;
    if (kind === null || typeof key !== 'string' || key === '') return null;
    return { kind, key };
  }

  /**
   * The stored subject, or null when there is none, it is of an unknown version or its shape is not a
   * subject's. A version-1 record is read as a subject without a compared set.
   */
  private readStoredSubject(): CcStoredSubject | null {
    let stored: string | null;
    try {
      stored = localStorage.getItem(CC_SUBJECT_STORAGE_KEY);
    } catch {
      return null;
    }
    if (stored === null) return null;
    let record: unknown;
    try {
      record = JSON.parse(stored);
    } catch {
      return null;
    }
    if (!record || typeof record !== 'object') return null;
    const candidate = record as { version?: unknown; modelKey?: unknown; range?: unknown; compare?: unknown };
    if (candidate.version !== CC_SUBJECT_STORAGE_VERSION && candidate.version !== 1) return null;
    const modelKey = candidate.modelKey;
    if (typeof modelKey !== 'string' || modelKey === '') return null;
    const range = candidate.range as { preset?: unknown; fromDay?: unknown; toDay?: unknown; anchorUtc?: unknown } | null;
    if (!range || typeof range !== 'object') return null;
    const preset = CC_RANGE_PRESETS.find(entry => entry.id === range.preset)?.id;
    const fromDay = range.fromDay;
    const toDay = range.toDay;
    const anchorUtc = range.anchorUtc;
    if (preset === undefined || typeof fromDay !== 'string' || typeof toDay !== 'string') return null;
    if (anchorUtc !== null && typeof anchorUtc !== 'string') return null;
    return {
      version: CC_SUBJECT_STORAGE_VERSION,
      modelKey,
      range: { preset, fromDay, toDay, anchorUtc: typeof anchorUtc === 'string' ? anchorUtc : null },
      compare: candidate.version === CC_SUBJECT_STORAGE_VERSION ? ChatConsistencyTabComponent.readStoredCompare(candidate.compare) : null
    };
  }

  /**
   * Selects the stored model with its dates, a rolling preset moved to now, and its compared set as
   * the preferred one, when the model is among the models with runs. A stored model that is not is
   * forgotten.
   */
  private restoreSubject(): void {
    const stored = this.readStoredSubject();
    if (!stored) return;
    if (!this.axes.some(axis => axis.key === stored.modelKey && axis.runCount > 0)) {
      try {
        localStorage.removeItem(CC_SUBJECT_STORAGE_KEY);
      } catch {
        // Nothing to forget when storage is unavailable.
      }
      return;
    }
    this.selectedKey = stored.modelKey;
    this.range = ccAnchorRange(stored.range, new Date());
    this.compareKey = stored.compare?.key ?? null;
    this.loadSubject();
  }

  // --- The wizard dialog ---

  /** Opens the wizard on the given step, or where it was left. */
  openWizard(step?: CcWizardStep): void {
    this.wizardMounted = true;
    // The dialog's @if content has to exist before showModal(), or an empty dialog opens.
    this.cdr.detectChanges();
    const dialog = this.wizardDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    // The anchor-positioning polyfill does not observe DOM mutations, and the wizard's tooltips were
    // behind the @if until now.
    refreshAnchorPositioning();
    if (step !== undefined) {
      this.wizard?.goToStep(step);
    } else {
      // showModal() would otherwise focus the close button.
      this.wizard?.focusHeading();
    }
  }

  closeWizard(): void {
    // close() fires the dialog's (close) event, so the state is handled in one place.
    const dialog = this.wizardDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  /**
   * Escape, which reaches the dialog as (cancel) before (close). Refused while a chart export runs
   * or report charts are attached. A `cancel` does not bubble, so one from a nested dialog never
   * arrives here; the target check keeps that so for a synthetic one. Chrome makes a second
   * `cancel` without user activation in between non-cancelable, so the focused element is recorded
   * for the reopen in `onWizardClose`.
   */
  onWizardCancel(event: Event): void {
    if (event.target !== this.wizardDialog?.nativeElement) return;
    if (this.wizard?.closeBlocked) {
      event.preventDefault();
      this.focusBeforeCancel = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    }
  }

  /**
   * Nothing is torn down: the mounted content is what reopening preserves. The saved analyses are read
   * again, since the Results step's Reports section may have written documents from one of them. A close that gets
   * through while the wizard is blocked (a repeated Escape, or a browser without `closedby`) reopens
   * the dialog instead.
   */
  onWizardClose(event: Event): void {
    if (event.target !== this.wizardDialog?.nativeElement) return;
    if (this.destroyed) return;
    if (this.wizard?.closeBlocked) {
      this.reopenBlockedWizard();
      return;
    }
    this.loadAnalyses();
    this.cdr.markForCheck();
  }

  /** Shows the dialog again and puts focus back where it was, else on the wizard heading. */
  private reopenBlockedWizard(): void {
    const dialog = this.wizardDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    const previous = this.focusBeforeCancel;
    this.focusBeforeCancel = null;
    if (previous && dialog?.contains(previous)) {
      previous.focus();
    } else {
      this.wizard?.focusHeading();
    }
    this.cdr.markForCheck();
  }

  // --- Loading ---

  loadAxes(): void {
    this.axesLoading = true;
    this.axesError = null;
    this.axesSub?.unsubscribe();
    this.axesSub = this.service.listModels().subscribe({
      next: axes => {
        this.axesLoading = false;
        this.axes = axes;
        if (this.selectedKey === null) this.restoreSubject();
        this.cdr.markForCheck();
      },
      error: err => {
        this.axesLoading = false;
        this.axesError = ccErrorText(err, 'The models could not be loaded.');
        this.cdr.markForCheck();
      }
    });
  }

  loadAnalyses(): void {
    this.analysesLoading = true;
    this.analysesError = null;
    this.analysesSub?.unsubscribe();
    this.analysesSub = this.service.listAnalyses().subscribe({
      next: analyses => {
        this.analysesLoading = false;
        this.analyses = analyses;
        this.cdr.markForCheck();
      },
      error: err => {
        this.analysesLoading = false;
        this.analysesError = ccErrorText(err, 'The saved analyses could not be loaded.');
        this.cdr.markForCheck();
      }
    });
  }

  selectModel(key: string): void {
    if (key === this.selectedKey) return;
    this.selectedKey = key;
    this.timeline = null;
    this.rows = [];
    this.batteryRows = [];
    this.comparisonSets = null;
    this.compareKey = null;
    this.scope = CC_EMPTY_SCOPE;
    this.anchorError = null;
    this.announcement = '';
    this.persistSubject();
    this.loadSubject();
  }

  /**
   * Compares within another battery or suite. The selection counts the new set's units, so it is
   * cleared, and the clearing announced when there was one.
   */
  setCompare(key: string): void {
    if (key === this.compareKey || !this.comparisonSets?.sets.some(set => set.key === key)) return;
    const hadSelection = !scopeIsDefault(this.scope);
    this.compareKey = key;
    this.scope = ccEmptyScope(setUnitKind(key));
    this.announcement = hadSelection ? CC_SET_CHANGE_NOTE : '';
    this.persistSubject();
    this.cdr.markForCheck();
  }

  setRange(range: CcDateRange): void {
    this.range = range;
    this.persistSubject();
    this.loadSubject();
  }

  /** The step-1 unit selection changed. */
  setScope(scope: CcRunScope): void {
    this.scope = scope;
    this.cdr.markForCheck();
  }

  /** Reload runs: a rolling preset moves to now first. */
  reloadSubject(): void {
    this.range = ccAnchorRange(this.range, new Date());
    this.persistSubject();
    this.loadSubject();
  }

  /**
   * The timeline, the run table, the comparison sets and the battery runs of the subject over the
   * range, together. The compared set stays while it is offered, else becomes the server's default,
   * which clears the selection and says so. Marks and left-out units that no longer appear among the
   * set's units are dropped from the selection, and the drop is announced.
   */
  loadSubject(): void {
    const key = this.selectedKey;
    this.subjectSub?.unsubscribe();
    if (!key) return;
    const { fromUtc: from, toUtc: to } = ccRangeBounds(this.range);
    this.subjectLoading = true;
    this.timelineError = null;
    this.runsError = null;
    this.cdr.markForCheck();
    this.subjectSub = forkJoin({
      timeline: this.service.getTimeline(key, from, to),
      rows: this.service.getRuns(key, from, to),
      sets: this.service.getComparisonSets(key, from, to),
      batteryRows: this.service.getBatteryRuns(key, from, to)
    }).subscribe({
      next: ({ timeline, rows, sets, batteryRows }) => {
        if (this.selectedKey !== key) return;
        this.subjectLoading = false;
        this.timeline = timeline;
        this.rows = rows;
        this.batteryRows = Array.isArray(batteryRows) ? batteryRows : [];
        this.comparisonSets = sets && Array.isArray(sets.sets) ? sets : { sets: [], defaultKey: null };
        const offered = (candidate: string | null) => !!candidate && this.comparisonSets!.sets.some(set => set.key === candidate);
        const previousKey = this.compareKey;
        const nextKey = offered(previousKey) ? previousKey : (offered(this.comparisonSets.defaultKey) ? this.comparisonSets.defaultKey : null);
        const notes: string[] = [];
        if (nextKey !== previousKey) {
          if (!scopeIsDefault(this.scope)) notes.push(CC_SET_CHANGE_NOTE);
          this.compareKey = nextKey;
          this.scope = ccEmptyScope(setUnitKind(nextKey));
          this.persistSubject();
        }
        const pruned = pruneScope(this.scope, this.units());
        this.scope = pruned.scope;
        if (pruned.note) notes.push(pruned.note);
        if (notes.length > 0) this.announcement = notes.join(' ');
        this.cdr.markForCheck();
      },
      error: err => {
        if (this.selectedKey !== key) return;
        this.subjectLoading = false;
        this.timelineError = ccErrorText(err, 'The timeline could not be loaded.');
        this.runsError = this.timelineError;
        this.cdr.markForCheck();
      }
    });
  }

  // --- Run actions ---

  /**
   * Switches to Run Benchmark, so the wizard closes first. Refused while a chart export or a report
   * chart attachment runs, which the switch would tear down.
   */
  repeatSetup(runId: number): void {
    if (this.wizard?.closeBlocked) {
      this.announcement = 'Wait for the chart export or the report charts to finish, then repeat the setup.';
      this.cdr.markForCheck();
      return;
    }
    this.closeWizard();
    this.bridge.repeatRunSetup(runId);
  }

  /** The run report is a shell dialog opened after the wizard, so it shows above it. */
  openRunReport(runId: number): void {
    this.bridge.viewRunDetail(runId);
  }

  /** The Battery Run Report is a shell dialog too, as Run History opens it. */
  openBatteryRunReport(batteryRunId: number): void {
    this.bridge.openBatteryRunReport(batteryRunId);
  }

  /** Marks or unmarks the run as the grader anchor, and updates the row and its timeline point in place. */
  toggleAnchor(row: CcRunRow): void {
    if (this.anchorBusy.has(row.runId)) return;
    const isAnchor = !row.isAnchor;
    this.anchorBusy = new Set([...this.anchorBusy, row.runId]);
    this.anchorError = null;
    this.announcement = '';
    this.cdr.markForCheck();
    this.anchorSubs.get(row.runId)?.unsubscribe();
    this.anchorSubs.set(row.runId, this.service.setAnchor(row.runId, isAnchor).subscribe({
      next: response => {
        this.clearAnchorBusy(row.runId);
        const marked = response?.isAnchor ?? isAnchor;
        this.rows = this.rows.map(r => r.runId === row.runId ? { ...r, isAnchor: marked } : r);
        if (this.timeline) {
          this.timeline = {
            ...this.timeline,
            points: this.timeline.points.map(p => p.runId === row.runId ? { ...p, isAnchor: marked } : p)
          };
        }
        this.announcement = marked ? `Run #${row.runId} is the grader anchor.` : `Run #${row.runId} is no longer an anchor.`;
        this.cdr.markForCheck();
      },
      error: err => {
        this.clearAnchorBusy(row.runId);
        this.anchorError = `The anchor of run #${row.runId} could not be saved: ${ccErrorText(err, 'the request failed.')}`;
        this.cdr.markForCheck();
      }
    }));
  }

  private clearAnchorBusy(runId: number): void {
    const next = new Set(this.anchorBusy);
    next.delete(runId);
    this.anchorBusy = next;
    this.anchorSubs.delete(runId);
  }

  // --- Analyses ---

  onAnalysisSaved(result: CcAnalysisResult): void {
    this.loadAnalyses();
    this.announcement = result.analysisId !== null ? `Analysis #${result.analysisId} was saved.` : '';
    this.cdr.markForCheck();
  }

  /**
   * Opens a saved analysis in the wizard on its results, switching the subject to its model and step 1
   * to its compared set first. The step-1 run selection is cleared, since the analysis carries its own
   * record of the runs it used.
   */
  openAnalysis(id: number): void {
    this.openingId = id;
    this.openError = null;
    this.cdr.markForCheck();
    this.openSub?.unsubscribe();
    this.openSub = this.service.getAnalysis(id).subscribe({
      next: result => {
        this.openingId = null;
        const hadSelection = !scopeIsDefault(this.scope);
        if (result.subject.key && result.subject.key !== this.selectedKey) {
          this.selectModel(result.subject.key);
        }
        const setKey = result.comparisonSet?.key ?? null;
        let switched = false;
        if (setKey && setKey !== this.compareKey) {
          if (!this.comparisonSets) {
            // Still loading: the load keeps it while it is offered.
            this.compareKey = setKey;
          } else if (this.comparisonSets.sets.some(set => set.key === setKey)) {
            // Loaded: switched only when the set is offered in these dates.
            this.compareKey = setKey;
            this.persistSubject();
            switched = true;
          }
        }
        if (hadSelection || switched) {
          this.scope = ccEmptyScope(this.unitKind);
        }
        if (hadSelection) {
          this.announcement = 'The run selection in step 1 was cleared to show the saved analysis.';
        }
        // Renders the wizard with the new subject before the result is handed to it.
        this.openWizard();
        this.wizard?.showResult(result);
        this.cdr.markForCheck();
      },
      error: err => {
        this.openingId = null;
        this.openError = ccErrorText(err, 'The analysis could not be opened.');
        this.cdr.markForCheck();
      }
    });
  }

  onAnalysisDeleted(id: number): void {
    this.analyses = this.analyses.filter(analysis => analysis.id !== id);
    this.cdr.markForCheck();
  }

  onRunsChanged(): void {
    this.loadSubject();
  }

  onAnnotationsChanged(): void {
    this.loadSubject();
  }
}
