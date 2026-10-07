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
import {
  CcAnalysisResult,
  CcAnalysisSummary,
  CcModelAxis,
  CcOpenDocumentsRequest,
  CcRunRow,
  CcTimeline
} from '../chat-consistency.models';
import { CcDayRange, CcModelStepComponent } from '../model-step/model-step.component';
import { CcTimelineWorkspaceComponent } from '../timeline-workspace/timeline-workspace.component';

/** The steps of the Chat Consistency wizard. */
export type CcWizardStep = 1 | 2 | 3 | 4 | 5 | 6;

/**
 * Every wizard step with its title and a one-line summary of what it is for.
 *
 * Exported so the launcher lists the same steps under the same names as the wizard's own step tabs.
 */
export const CC_WIZARD_STEPS = [
  { step: 1, title: 'Model', summary: 'Choose the model and the dates, and review its runs.' },
  { step: 2, title: 'Timeline', summary: 'Every measure over the dates, with the Overseer changes, annotations and served-model changes.' },
  { step: 3, title: 'Periods', summary: 'Choose the baseline and comparison periods, from a preset or by hand, under Protocol V1.' },
  { step: 4, title: 'Runs and controls', summary: 'Choose the runs and control runs, re-grade if needed, and analyze.' },
  { step: 5, title: 'Results', summary: 'The verdicts, their attribution and the next runs that would settle open questions.' },
  { step: 6, title: 'Reports', summary: 'Write the Chat Consistency Report documents.' }
] as const;

/** The steps the shared analysis component shows. */
type CcAnalysisWizardStep = 3 | 4 | 5 | 6;

const ANALYSIS_STEP_OF: Readonly<Record<CcAnalysisWizardStep, CcAnalysisStep>> = {
  3: 'periods',
  4: 'runs',
  5: 'results',
  6: 'reports'
};

const NEXT_LABELS: Readonly<Record<CcWizardStep, string>> = {
  1: 'Next: Timeline',
  2: 'Next: Periods',
  3: 'Next: Runs and controls',
  4: 'Analyze',
  5: 'Next: Reports',
  6: 'Close'
};

const NO_MODEL_REASON = 'Choose a model first.';
const NO_PERIODS_REASON = 'Choose the periods first.';
const NO_RESULT_REASON = 'Analyze first, or open a saved analysis.';

/**
 * The Chat Consistency wizard: the whole content of the full-screen dialog the tab declares — header,
 * step tabs, step panels and footer. Step 1 chooses the model and the dates, step 2 is the chart
 * workspace, and steps 3–6 are one shared analysis component shown one step at a time.
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
  @Input() range: CcDayRange = { fromDay: '', toDay: '' };
  @Input() timeline: CcTimeline | null = null;
  @Input() rows: readonly CcRunRow[] = [];
  /** The timeline and the run table of the subject are loading. */
  @Input() loading = false;
  @Input() timelineError: string | null = null;
  @Input() runsError: string | null = null;
  /** Runs whose anchor mark is being saved. */
  @Input() anchorBusy: ReadonlySet<number> = new Set<number>();
  @Input() anchorError: string | null = null;
  /** A one-off confirmation for the run table's status line. */
  @Input() announcement = '';

  // --- Steps 3–6 ---
  @Input() analyses: readonly CcAnalysisSummary[] = [];
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() pickerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;

  @Output() readonly modelChange = new EventEmitter<string>();
  @Output() readonly rangeChange = new EventEmitter<CcDayRange>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly anchorToggle = new EventEmitter<CcRunRow>();
  @Output() readonly openRunReport = new EventEmitter<number>();
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
  /** The shared analysis component exists: steps 3–6 have been entered, or a saved analysis opened. */
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

  /** `GPT-5 high · 18 runs · every date`, or the invitation to choose a model. */
  get subtitle(): string {
    const axis = this.axis;
    if (!axis) return 'Choose a model, then follow the steps';
    const count = this.timeline?.points.length ?? axis.runCount;
    return `${axis.displayName} · ${plural(count, 'run')} · ${this.rangeText}`;
  }

  private get rangeText(): string {
    const { fromDay, toDay } = this.range;
    if (fromDay && toDay) return `${fromDay} to ${toDay}`;
    if (fromDay) return `from ${fromDay}`;
    if (toDay) return `until ${toDay}`;
    return 'every date';
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
   * Reports step draws and uploads report charts: closing would strand a half-written batch.
   */
  get closeBlocked(): boolean {
    return this.workspaceExporting || (this.analysis?.chartsAttaching ?? false);
  }

  isVisited(step: CcWizardStep): boolean {
    return this.visitedSteps.has(step);
  }

  /** The panel a step tab controls: steps 3–6 share the analysis panel. */
  panelId(step: CcWizardStep): string {
    return step >= 3 ? 'cc-step-panel-analysis' : `cc-step-panel-${step}`;
  }

  /**
   * Step 1 is always open; steps 2 and 3 need a model; step 4 valid periods and overrides; steps 5
   * and 6 a result, analyzed or opened from the saved analyses.
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
      case 4:
        return this.axis !== null && !!this.analysis && this.analysis.reachable('runs');
      default:
        return !!this.analysis && this.analysis.reachable(step === 5 ? 'results' : 'reports');
    }
  }

  /** Why a step cannot be opened, for its tab's description; empty where it can. */
  stepBlockedReason(step: CcWizardStep): string {
    if (this.isStepReachable(step)) return '';
    if (step <= 4 && this.axis === null) return NO_MODEL_REASON;
    if (step === 4) {
      return (this.analysis ? this.analysis.periodsError || this.analysis.overridesError : '') || NO_PERIODS_REASON;
    }
    return NO_RESULT_REASON;
  }

  // --- Footer ---

  get canGoPrevious(): boolean {
    return this.step > 1;
  }

  get nextLabel(): string {
    return NEXT_LABELS[this.step];
  }

  /** Why Next (Analyze on step 4) is unavailable, named beside it; empty while it is available or busy. */
  get nextBlockedReason(): string {
    switch (this.step) {
      case 4:
        if (this.analyzing) return '';
        return this.analysis ? this.analysis.analyzeBlocked : NO_PERIODS_REASON;
      case 6:
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

  /** The next step; Analyze on step 4, which moves on when the analysis is saved; Close on step 6. */
  nextStep(): void {
    if (this.step === 6) {
      if (!this.closeBlocked) this.closeRequested.emit();
      return;
    }
    if (!this.canGoNext) return;
    if (this.step === 4) {
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

  /** Shows a reachable step and focuses its heading. */
  goToStep(step: CcWizardStep): void {
    if (this.selectStep(step)) this.focusStepHeading();
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
   * step 5 — the one path to Results that does not pass step 3.
   */
  showResult(result: CcAnalysisResult): void {
    this.mountAnalysis();
    this.analysis?.showResult(result);
    this.goToStep(5);
  }

  /**
   * The analysis was saved: the tab hears of it, and the wizard moves to Results if it is still on
   * Runs and controls. From any other step Results only becomes reachable.
   */
  onAnalysisSaved(result: CcAnalysisResult): void {
    this.analysisSaved.emit(result);
    if (this.step === 4) this.goToStep(5);
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
      if (step === 4 && this.step !== 4) this.analysis?.preselectRuns();
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

  private focusStepHeading(): void {
    const id = this.step >= 3 ? 'cc-step-heading-analysis' : `cc-step-heading-${this.step}`;
    this.host.nativeElement.querySelector<HTMLElement>(`#${id}`)?.focus();
  }
}
