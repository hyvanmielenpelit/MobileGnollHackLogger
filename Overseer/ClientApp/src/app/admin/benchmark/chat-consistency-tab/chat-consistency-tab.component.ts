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
import { endOfUtcDay, formatInteger, formatUtcDate, plural, startOfUtcDay, utcMillis } from './chat-consistency-format';
import {
  CcAnalysisResult,
  CcAnalysisSummary,
  CcModelAxis,
  CcOpenDocumentsRequest,
  CcRunRow,
  CcTimeline
} from './chat-consistency.models';
import { CcDayRange } from './model-step/model-step.component';
import { CcSavedAnalysesComponent } from './saved-analyses/saved-analyses.component';

/** Where the launcher's *How chat consistency works* state is kept, per browser. */
export const CC_LAUNCHER_STORAGE_KEY = 'overseer.benchmark.chatConsistency.launcher';

/** The version of the stored launcher record; a record of another version reads as none. */
const CC_LAUNCHER_STORAGE_VERSION = 1;

/**
 * The Chat Consistency sub-tab: whether the Overseer chat with one model stayed the same over time.
 * A launcher page — what the view does, the current model, how it works and the saved analyses —
 * opens the six-step wizard in a full-screen dialog.
 *
 * It owns the subject, the date range and their data, and performs the run actions the wizard asks
 * for: anchors through the API, *Repeat this run's setup* and *Open run report* through the shell.
 */
@Component({
  selector: 'app-chat-consistency-tab',
  standalone: true,
  imports: [CcWizardComponent, CcSavedAnalysesComponent],
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
  range: CcDayRange = { fromDay: '', toDay: '' };
  timeline: CcTimeline | null = null;
  rows: CcRunRow[] = [];
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

  constructor() {
    // The configurations the pickers offer arrive through the shell; OnPush needs telling.
    inject(BenchmarkViewSync).changed$.pipe(takeUntilDestroyed()).subscribe(() => this.cdr.markForCheck());
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.restoreHowItWorks();
    this.loadAxes();
    this.loadAnalyses();
  }

  ngOnDestroy(): void {
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

  // --- Launcher read-outs ---

  /** `6 runs, 4 with call telemetry`, over every date, and how many fall in the chosen dates. */
  get currentRunsText(): string {
    const axis = this.selectedAxis;
    if (!axis) return '';
    const all = `${plural(axis.runCount, 'run')}, ${formatInteger(axis.telemetryRunCount)} with call telemetry`;
    const ranged = this.range.fromDay || this.range.toDay;
    return ranged && this.timeline ? `${all} · ${formatInteger(this.timeline.points.length)} in the chosen dates` : all;
  }

  get currentDatesText(): string {
    const { fromDay, toDay } = this.range;
    if (fromDay && toDay) return `${fromDay} to ${toDay}`;
    if (fromDay) return `From ${fromDay}`;
    if (toDay) return `Until ${toDay}`;
    return 'Every date';
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

  savedDay(analysis: CcAnalysisSummary): string {
    return formatUtcDate(analysis.createdAtUtc);
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
   * arrives here; the target check keeps that so for a synthetic one.
   */
  onWizardCancel(event: Event): void {
    if (event.target !== this.wizardDialog?.nativeElement) return;
    if (this.wizard?.closeBlocked) event.preventDefault();
  }

  /**
   * Nothing is torn down: the mounted content is what reopening preserves. The saved analyses are read
   * again, since the Reports step may have written documents from one of them.
   */
  onWizardClose(event: Event): void {
    if (event.target !== this.wizardDialog?.nativeElement) return;
    this.loadAnalyses();
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
    this.anchorError = null;
    this.announcement = '';
    this.loadSubject();
  }

  setRange(range: CcDayRange): void {
    this.range = range;
    this.loadSubject();
  }

  /** The timeline and the run table of the subject over the range, together. */
  loadSubject(): void {
    const key = this.selectedKey;
    this.subjectSub?.unsubscribe();
    if (!key) return;
    const from = this.range.fromDay ? startOfUtcDay(this.range.fromDay) : null;
    const to = this.range.toDay ? endOfUtcDay(this.range.toDay) : null;
    this.subjectLoading = true;
    this.timelineError = null;
    this.runsError = null;
    this.cdr.markForCheck();
    this.subjectSub = forkJoin({
      timeline: this.service.getTimeline(key, from, to),
      rows: this.service.getRuns(key, from, to)
    }).subscribe({
      next: ({ timeline, rows }) => {
        if (this.selectedKey !== key) return;
        this.subjectLoading = false;
        this.timeline = timeline;
        this.rows = rows;
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

  /** Opens a saved analysis in the wizard on its results, switching the subject to its model first. */
  openAnalysis(id: number): void {
    this.openingId = id;
    this.openError = null;
    this.cdr.markForCheck();
    this.openSub?.unsubscribe();
    this.openSub = this.service.getAnalysis(id).subscribe({
      next: result => {
        this.openingId = null;
        if (result.subject.key && result.subject.key !== this.selectedKey) {
          this.selectModel(result.subject.key);
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
