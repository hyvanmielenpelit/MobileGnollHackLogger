import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  inject
} from '@angular/core';

import { SystemAiConfigDto } from '../../../../services/admin.service';
import { ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import { CcAnalysisStep, CcAnalysisWizardComponent } from '../analysis-wizard/analysis-wizard.component';
import { plural } from '../chat-consistency-format';
import { CC_ALL_DATES, CcDateRange, ccDateRangeText } from '../chat-consistency-range';
import {
  CC_EMPTY_SCOPE,
  CcRunInclusion,
  CcRunScope,
  CcScopeUnit,
  batterySetRuns,
  comparisonSetUnits,
  isBatteryRunRow,
  notAnalyzedSetRuns,
  notAnalyzedUnits,
  scopeIsDefault,
  scopeKey,
  scopeRuns,
  scopedMemberRuns,
  setUnitKind,
  suiteSetRuns
} from '../chat-consistency-scope';
import {
  CcAnalysisResult,
  CcAnalysisSummary,
  CcBatteryRunRow,
  CcComparisonSet,
  CcComparisonSets,
  CcModelAxis,
  CcOpenDocumentsRequest,
  CcRunRow,
  CcTimeline,
  CcUnitKind
} from '../chat-consistency.models';
import { CcModelStepComponent } from '../model-step/model-step.component';
import { CcTimelineWorkspaceComponent } from '../timeline-workspace/timeline-workspace.component';

/** The steps of the Chat Consistency wizard. */
export type CcWizardStep = 1 | 2 | 3 | 4;

/**
 * Every wizard step with its title and a one-line summary of what it is for.
 *
 * Exported so the launcher lists the same steps under the same names as the wizard's own step tabs.
 */
export const CC_WIZARD_STEPS = [
  { step: 1, title: 'Model', summary: 'Choose the model, the dates and the runs the analysis uses.' },
  { step: 2, title: 'Timeline', summary: 'Every measure over the dates, with the Overseer changes, annotations and served-model changes.' },
  { step: 3, title: 'Analyze', summary: 'Split the chosen runs into a baseline and a comparison, review the controls and the protocol, and analyze.' },
  { step: 4, title: 'Results', summary: 'The verdicts, their attribution, the next runs, and the Chat Consistency Report documents.' }
] as const;

/** The steps the shared analysis component shows. */
type CcAnalysisWizardStep = 3 | 4;

const ANALYSIS_STEP_OF: Readonly<Record<CcAnalysisWizardStep, CcAnalysisStep>> = {
  3: 'analyze',
  4: 'results'
};

const NEXT_LABELS: Readonly<Record<CcWizardStep, string>> = {
  1: 'Next: Timeline',
  2: 'Next: Analyze',
  3: 'Analyze',
  4: 'Close'
};

const NO_MODEL_REASON = 'Choose a model first.';
const NO_PERIODS_REASON = 'Choose the periods first.';
const NO_RESULT_REASON = 'Analyze first, or open a saved analysis.';

/**
 * The Chat Consistency wizard: the whole content of the full-screen dialog the tab declares — header,
 * step tabs, step panels and footer. Step 1 chooses the model and the dates, step 2 is the chart
 * workspace, and steps 3 and 4 are one shared analysis component shown one step at a time.
 *
 * Every step is mounted on its first visit and afterwards kept, hidden while another step shows, so
 * a table's sort and page, the chart zoom and the scroll positions survive a step change. The tab
 * owns the data and performs the actions the steps ask for.
 */
@Component({
  selector: 'app-cc-wizard',
  standalone: true,
  imports: [CcModelStepComponent, CcTimelineWorkspaceComponent, CcAnalysisWizardComponent],
  templateUrl: './cc-wizard.component.html',
  styleUrls: ['./cc-wizard.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcWizardComponent {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  // --- Step 1 ---
  @Input() axes: readonly CcModelAxis[] = [];
  @Input() axesLoading = false;
  @Input() axesError: string | null = null;
  @Input() selectedKey: string | null = null;
  @Input() range: CcDateRange = CC_ALL_DATES;
  /** The units of step 1 the analysis uses: runs, or battery runs in a battery set. */
  @Input() scope: CcRunScope = CC_EMPTY_SCOPE;
  /** The batteries and suites the subject can be compared within; null until loaded. */
  @Input() comparisonSets: CcComparisonSets | null = null;
  /** The compared set; null while none is (the runs are then analyzed one by one). */
  @Input() compareKey: string | null = null;
  @Input() timeline: CcTimeline | null = null;
  /** Every run of the subject in the dates. */
  @Input() rows: readonly CcRunRow[] = [];
  /** Every battery run of the subject in the dates. */
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  /** The timeline and the run table of the subject are loading. */
  @Input() loading = false;
  @Input() timelineError: string | null = null;
  @Input() runsError: string | null = null;
  /** Runs whose anchor mark is being saved. */
  @Input() anchorBusy: ReadonlySet<number> = new Set<number>();
  @Input() anchorError: string | null = null;
  /** A one-off confirmation for the run table's status line. */
  @Input() announcement = '';

  // --- Steps 3 and 4 ---
  @Input() analyses: readonly CcAnalysisSummary[] = [];
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() pickerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;

  @Output() readonly modelChange = new EventEmitter<string>();
  @Output() readonly rangeChange = new EventEmitter<CcDateRange>();
  @Output() readonly compareChange = new EventEmitter<string>();
  @Output() readonly scopeChange = new EventEmitter<CcRunScope>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly anchorToggle = new EventEmitter<CcRunRow>();
  @Output() readonly openRunReport = new EventEmitter<number>();
  @Output() readonly openBatteryRunReport = new EventEmitter<number>();
  @Output() readonly analysisSaved = new EventEmitter<CcAnalysisResult>();
  @Output() readonly runsChanged = new EventEmitter<void>();
  @Output() readonly annotationsChanged = new EventEmitter<void>();
  @Output() readonly openDocuments = new EventEmitter<CcOpenDocumentsRequest>();
  /** Reload the timeline and the run table of the subject. */
  @Output() readonly reload = new EventEmitter<void>();
  @Output() readonly closeRequested = new EventEmitter<void>();

  @ViewChild(CcAnalysisWizardComponent) analysis?: CcAnalysisWizardComponent;
  @ViewChild(CcTimelineWorkspaceComponent) timelineWorkspace?: CcTimelineWorkspaceComponent;
  @ViewChild('wizardTitle') wizardTitle?: ElementRef<HTMLElement>;

  readonly steps = CC_WIZARD_STEPS;
  readonly stepTitles = Object.fromEntries(
    CC_WIZARD_STEPS.map(entry => [entry.step, entry.title])
  ) as Record<CcWizardStep, string>;

  step: CcWizardStep = 1;
  /** Steps 1 and 2 once shown; their panels stay mounted afterwards. */
  private readonly visitedSteps = new Set<CcWizardStep>([1]);
  /** The shared analysis component exists: step 3 or 4 has been entered, or a saved analysis opened. */
  analysisMounted = false;
  /** The step the analysis component shows; kept while steps 1–2 show. */
  analysisWizardStep: CcAnalysisWizardStep = 3;

  // --- State read from the inputs and the steps ---

  get axis(): CcModelAxis | null {
    return this.axes.find(axis => axis.key === this.selectedKey) ?? null;
  }

  get analysisStep(): CcAnalysisStep {
    return ANALYSIS_STEP_OF[this.analysisWizardStep];
  }

  /** The compared set, when the sets offer it. */
  get compareSet(): CcComparisonSet | null {
    return this.comparisonSets?.sets.find(set => set.key === this.compareKey) ?? null;
  }

  /**
   * `GPT-5 high · 18 runs · Last 30 days`, or with a set `Claude 5.5 Haiku (xhigh) · Two initial suites
   * (revision 1) · 2 battery runs · Last 30 days`, with `· 15 in the analysis` while step 1 narrows the
   * units, or the invitation to choose a model.
   */
  get subtitle(): string {
    const axis = this.axis;
    if (!axis) return 'Choose a model, then follow the steps';
    const set = this.compareSet;
    let text: string;
    if (set) {
      const noun = set.kind === 'battery' ? 'battery run' : 'run';
      text = `${axis.displayName} · ${set.label} · ${plural(this.scopeState().units.length, noun)} · ${this.rangeText}`;
    } else {
      const count = this.timeline?.points.length ?? axis.runCount;
      text = `${axis.displayName} · ${plural(count, 'run')} · ${this.rangeText}`;
    }
    return scopeIsDefault(this.scope) && !this.hasIncompleteUnits ? text : `${text} · ${this.scopeState().scoped.length} in the analysis`;
  }

  get rangeText(): string {
    return ccDateRangeText(this.range);
  }

  /** A battery run of the set is never analyzed for want of a usable member, so the units are narrowed without a selection. */
  private get hasIncompleteUnits(): boolean {
    return this.scopeState().units.some(unit => isBatteryRunRow(unit) && !unit.complete);
  }

  /** The runs the analysis uses, oldest first: in a battery set, the members of the battery runs it uses. */
  get scopedRows(): readonly CcRunRow[] {
    return this.scopeState().scopedRows;
  }

  /** The battery runs the analysis uses, oldest first; empty unless a battery set is compared. */
  get scopedBatteryRows(): readonly CcBatteryRunRow[] {
    return this.scopeState().scopedBatteryRows;
  }

  /** Every battery run of the compared battery set in the dates. */
  get setBatteryRows(): readonly CcBatteryRunRow[] {
    return this.scopeState().setBatteryRows;
  }

  /** Every run of the compared suite set in the dates; every run without a suite set. */
  get setRows(): readonly CcRunRow[] {
    return this.scopeState().setRows;
  }

  /** The runs of step 1 not in the analysis, with why; runs outside the compared set included. */
  get notAnalyzed(): ReadonlyMap<number, CcRunInclusion> {
    return this.scopeState().notAnalyzed;
  }

  /** In a battery set, the battery runs not in the analysis, keyed by battery run id, with why. */
  get notAnalyzedUnits(): ReadonlyMap<number, CcRunInclusion> {
    return this.scopeState().notAnalyzedUnits;
  }

  /** What step 1 counts, and so what the timeline plots: battery runs in a battery set, runs otherwise. */
  get unitKind(): CcUnitKind {
    return setUnitKind(this.compareSet?.key);
  }

  get scopeKeyValue(): string {
    return this.scopeState().key;
  }

  /** The scope's derived values, kept until the rows, the battery rows, the set or the scope object change. */
  private scopeMemo: {
    rows: readonly CcRunRow[];
    batteryRows: readonly CcBatteryRunRow[];
    compareKey: string | null;
    scope: CcRunScope;
    units: readonly CcScopeUnit[];
    scoped: readonly CcScopeUnit[];
    scopedRows: readonly CcRunRow[];
    scopedBatteryRows: readonly CcBatteryRunRow[];
    setBatteryRows: readonly CcBatteryRunRow[];
    setRows: readonly CcRunRow[];
    notAnalyzed: ReadonlyMap<number, CcRunInclusion>;
    notAnalyzedUnits: ReadonlyMap<number, CcRunInclusion>;
    key: string;
  } | null = null;

  private scopeState(): NonNullable<CcWizardComponent['scopeMemo']> {
    const memo = this.scopeMemo;
    const setKey = this.compareSet?.key ?? null;
    if (memo && memo.rows === this.rows && memo.batteryRows === this.batteryRows && memo.compareKey === setKey
      && memo.scope === this.scope) {
      return memo;
    }
    const units = comparisonSetUnits(this.rows, this.batteryRows, setKey);
    const scoped = scopeRuns(units, this.scope);
    this.scopeMemo = {
      rows: this.rows,
      batteryRows: this.batteryRows,
      compareKey: setKey,
      scope: this.scope,
      units,
      scoped,
      // Without a battery set the units are runs, and the scoped list itself is handed on.
      scopedRows: scoped.some(isBatteryRunRow) ? scopedMemberRuns(scoped) : scoped as readonly CcRunRow[],
      scopedBatteryRows: scoped.filter(isBatteryRunRow),
      setBatteryRows: batterySetRuns(this.batteryRows, setKey),
      setRows: suiteSetRuns(this.rows, setKey),
      notAnalyzed: notAnalyzedSetRuns(this.rows, this.batteryRows, setKey, this.scope),
      notAnalyzedUnits: notAnalyzedUnits(this.batteryRows, setKey, this.scope),
      key: scopeKey(this.scope, setKey)
    };
    return this.scopeMemo;
  }

  /** Reload runs, on steps 1–2, refuses while the subject loads and without a model. */
  get reloadBlocked(): boolean {
    return this.loading || this.axis === null || this.workspaceExporting;
  }

  get workspaceExporting(): boolean {
    return this.timelineWorkspace?.exporting ?? false;
  }

  get analyzing(): boolean {
    return this.analysis?.analyzing ?? false;
  }

  /**
   * The wizard's close controls, and Escape through the tab, refuse while a chart export runs or the
   * Results step's Reports section draws and uploads report charts: closing would strand a half-written
   * batch.
   */
  get closeBlocked(): boolean {
    return this.workspaceExporting || (this.analysis?.chartsAttaching ?? false);
  }

  /** Why step 1's model, dates and compared set cannot change now; empty while they can. */
  get subjectLockedReason(): string {
    if (this.workspaceExporting) return 'The model and the dates are locked while the charts export.';
    if (this.analysis?.chartsAttaching) return 'The model and the dates are locked while report charts are attached.';
    return '';
  }

  isVisited(step: CcWizardStep): boolean {
    return this.visitedSteps.has(step);
  }

  /** The panel a step tab controls: steps 3 and 4 share the analysis panel. */
  panelId(step: CcWizardStep): string {
    return step >= 3 ? 'cc-step-panel-analysis' : `cc-step-panel-${step}`;
  }

  /**
   * Step 1 is always open; steps 2 and 3 need a model; step 4 a result, analyzed or opened from the
   * saved analyses.
   *
   * An unreachable step is `aria-disabled`, not `disabled`, so it stays in the focus order and its
   * reason is read with it.
   */
  isStepReachable(step: CcWizardStep): boolean {
    switch (step) {
      case 1:
        return true;
      case 2:
      case 3:
        return this.axis !== null;
      default:
        return !!this.analysis && this.analysis.reachable('results');
    }
  }

  /** Why a step cannot be opened, for its tab's description; empty where it can. */
  stepBlockedReason(step: CcWizardStep): string {
    if (this.isStepReachable(step)) return '';
    if (step <= 3) return NO_MODEL_REASON;
    return NO_RESULT_REASON;
  }

  // --- Footer ---

  get canGoPrevious(): boolean {
    return this.step > 1;
  }

  get nextLabel(): string {
    return NEXT_LABELS[this.step];
  }

  /** Why Next (Analyze on step 3) is unavailable, named beside it; empty while it is available or busy. */
  get nextBlockedReason(): string {
    switch (this.step) {
      case 3:
        if (this.analyzing) return '';
        return this.analysis ? this.analysis.analyzeBlocked : NO_PERIODS_REASON;
      case 4:
        return '';
      default:
        return this.stepBlockedReason((this.step + 1) as CcWizardStep);
    }
  }

  get canGoNext(): boolean {
    return this.nextBlockedReason === '' && !this.analyzing;
  }

  /** The nearest earlier step that can be opened. */
  previousStep(): void {
    if (!this.canGoPrevious) return;
    for (let step = this.step - 1; step >= 1; step--) {
      if (this.isStepReachable(step as CcWizardStep)) {
        this.goToStep(step as CcWizardStep);
        return;
      }
    }
  }

  /** The next step; Analyze on step 3, which moves on when the analysis is saved; Close on step 4. */
  nextStep(): void {
    if (this.step === 4) {
      if (!this.closeBlocked) this.closeRequested.emit();
      return;
    }
    if (!this.canGoNext) return;
    if (this.step === 3) {
      this.analysis?.analyze();
      return;
    }
    this.goToStep((this.step + 1) as CcWizardStep);
  }

  stopAnalyze(): void {
    this.analysis?.stopAnalyze();
  }

  onCloseClick(): void {
    if (!this.closeBlocked) this.closeRequested.emit();
  }

  onReload(): void {
    if (this.reloadBlocked) return;
    this.reload.emit();
  }

  // --- Steps ---

  /** Shows a reachable step and focuses its panel. */
  goToStep(step: CcWizardStep): void {
    if (this.selectStep(step)) this.focusStepPanel();
  }

  /**
   * Roving-tabindex keyboard support required by role="tablist": Left/Right move between steps and
   * wrap around, Home/End jump to the ends. Focus stays on the tabs, and moves even onto a step that
   * refuses to open, so its reason is read.
   */
  onStepKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.steps.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) return;

    event.preventDefault();
    const next = this.steps[(requested + this.steps.length) % this.steps.length].step;
    this.selectStep(next);
    this.host.nativeElement.querySelector<HTMLElement>(`#cc-step-tab-${next}`)?.focus();
  }

  /** Called by the tab after showModal(), which would otherwise focus the close button. */
  focusHeading(): void {
    this.wizardTitle?.nativeElement.focus();
  }

  /**
   * Opens a saved analysis on Results: mounts the analysis component, hands it the result and selects
   * step 4 — the one path to Results that does not pass step 3.
   */
  showResult(result: CcAnalysisResult): void {
    // Mounted on Results, so the Analyze body (and its re-grade panel) is not created unseen.
    this.analysisWizardStep = 4;
    this.mountAnalysis();
    this.analysis?.showResult(result);
    this.goToStep(4);
  }

  /**
   * The analysis was saved: the tab hears of it, and the wizard moves to Results if it is still on
   * Analyze. From any other step Results only becomes reachable.
   */
  onAnalysisSaved(result: CcAnalysisResult): void {
    this.analysisSaved.emit(result);
    if (this.step === 3) this.goToStep(4);
    else this.cdr.markForCheck();
  }

  /**
   * The analysis component's footer state may have changed. It can say so from its `ngOnChanges`,
   * while this view is being checked after the step tabs were already read, so the view is marked
   * again once that pass is over.
   */
  onAnalysisStateChange(): void {
    this.cdr.markForCheck();
    queueMicrotask(() => this.cdr.markForCheck());
  }

  onExportingChange(): void {
    this.cdr.markForCheck();
  }

  /** Selects a reachable step and renders it; false when the step cannot be opened. */
  private selectStep(step: CcWizardStep): boolean {
    if (!this.isStepReachable(step)) return false;
    if (step >= 3) {
      this.analysisWizardStep = step as CcAnalysisWizardStep;
      this.mountAnalysis();
      if (step === 3 && this.step !== 3) this.analysis?.preselectRuns();
    } else {
      this.visitedSteps.add(step);
    }
    this.step = step;
    this.cdr.detectChanges();
    // The anchor-positioning polyfill does not observe DOM mutations, and a newly shown step brings
    // interestfor tooltips.
    refreshAnchorPositioning();
    this.cdr.markForCheck();
    return true;
  }

  /** Creates the shared analysis component once, rendered so its view query resolves at once. */
  private mountAnalysis(): void {
    if (this.analysisMounted) return;
    this.analysisMounted = true;
    this.cdr.detectChanges();
  }

  private focusStepPanel(): void {
    this.host.nativeElement.querySelector<HTMLElement>(`#${this.panelId(this.step)}`)?.focus();
  }
}
