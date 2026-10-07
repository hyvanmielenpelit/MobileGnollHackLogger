import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import { SystemAiConfigDto } from '../../../../services/admin.service';
import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import {
  addUtcDays,
  controlRunsText,
  endOfUtcDay,
  formatUtcDate,
  formatUtcDateTime,
  isRunEligible,
  isUtcDateInput,
  startOfUtcDay,
  utcMillis,
  withinUtcDays
} from '../chat-consistency-format';
import {
  CcAnalysisRequest,
  CcAnalysisResult,
  CcAnalysisSummary,
  CcAnnotation,
  CcEvent,
  CcModelAxis,
  CcOpenDocumentsRequest,
  CcProtocolOverrides,
  CcRunRow,
  CcTimeline,
  CcTimelinePoint
} from '../chat-consistency.models';
import { CcRegradePanelComponent } from './regrade-panel.component';
import { CcReportsStepComponent } from './reports-step.component';
import { CcResultsViewComponent } from './results-view.component';

export type CcWizardStep = 1 | 2 | 3 | 4;

/** The wizard's steps, in order. */
export const CC_WIZARD_STEPS: readonly { readonly step: CcWizardStep; readonly title: string }[] = [
  { step: 1, title: 'Subject and periods' },
  { step: 2, title: 'Runs' },
  { step: 3, title: 'Results' },
  { step: 4, title: 'Reports' }
];

/** Protocol V1's primary endpoints as the server publishes them (`ChatConsistencyProtocol.V1`). */
export const CC_PROTOCOL_V1_ENDPOINTS: readonly {
  readonly id: string; readonly name: string; readonly margin: number; readonly unit: 'index points' | '%';
}[] = [
  { id: 'P1', name: 'Quality', margin: 3, unit: 'index points' },
  { id: 'P2', name: 'Time to first answer text', margin: 15, unit: '%' },
  { id: 'P3', name: 'Answer streaming rate', margin: 10, unit: '%' },
  { id: 'P4', name: 'Work per turn', margin: 15, unit: '%' },
  { id: 'P5', name: 'Cost per question', margin: 10, unit: '%' }
];

/** Protocol V1's α and minimum samples. */
export const CC_PROTOCOL_V1 = Object.freeze({
  alpha: 0.05,
  minimumRunsPerPeriod: 2,
  minimumDaysPerPeriod: 2,
  minimumPairedItems: 20,
  minimumSpeedRunsPerStratum: 3
});

export type CcPreset = 'launch' | 'annotation' | 'event' | 'later' | 'custom';

/** The days on either side of an annotation or an event that the before-and-after presets take. */
export const CC_PRESET_WINDOW_DAYS = 28;

export interface CcPeriodDays {
  baselineStart: string;
  baselineEnd: string;
  comparisonStart: string;
  comparisonEnd: string;
}

/** The first and last run day of a subject. */
function seriesDays(axis: CcModelAxis): { first: string; last: string } {
  return { first: formatUtcDate(axis.firstRunAtUtc), last: formatUtcDate(axis.lastRunAtUtc) };
}

function maxDay(a: string, b: string): string {
  return a > b ? a : b;
}

function minDay(a: string, b: string): string {
  return a < b ? a : b;
}

/** *Launch vs last 14 days*: the first 14 days of the series against its last 14, never overlapping. */
export function launchPreset(axis: CcModelAxis): CcPeriodDays {
  const { first, last } = seriesDays(axis);
  const comparisonStart = maxDay(addUtcDays(last, -13), first);
  return {
    baselineStart: first,
    baselineEnd: minDay(addUtcDays(first, 13), addUtcDays(comparisonStart, -1)),
    comparisonStart,
    comparisonEnd: last
  };
}

/** *Before vs after*: up to {@link CC_PRESET_WINDOW_DAYS} days before the day of `atUtc` against that day and the days after. */
export function aroundPreset(axis: CcModelAxis, atUtc: string): CcPeriodDays {
  const { first, last } = seriesDays(axis);
  const day = formatUtcDate(atUtc);
  return {
    baselineStart: maxDay(first, addUtcDays(day, -CC_PRESET_WINDOW_DAYS)),
    baselineEnd: addUtcDays(day, -1),
    comparisonStart: day,
    comparisonEnd: minDay(last, addUtcDays(day, CC_PRESET_WINDOW_DAYS - 1))
  };
}

/**
 * *Confirm on later data*: the last analysis's baseline again, against the runs after its last look —
 * from the day after it was saved to the subject's last run.
 */
export function laterDataPreset(axis: CcModelAxis, last: CcAnalysisSummary): CcPeriodDays {
  return {
    baselineStart: formatUtcDate(last.baselineStartUtc),
    baselineEnd: formatUtcDate(last.baselineEndUtc),
    comparisonStart: addUtcDays(formatUtcDate(last.createdAtUtc), 1),
    comparisonEnd: seriesDays(axis).last
  };
}

/** Why the periods cannot be analyzed, or '' when they can. */
export function periodsRefusal(days: CcPeriodDays): string {
  const all = [days.baselineStart, days.baselineEnd, days.comparisonStart, days.comparisonEnd];
  if (all.some(day => !isUtcDateInput(day))) return 'Enter all four dates.';
  if (days.baselineStart > days.baselineEnd) return 'The baseline must not end before it starts.';
  if (days.comparisonStart > days.comparisonEnd) return 'The comparison must not end before it starts.';
  if (days.comparisonStart <= days.baselineEnd) return 'The comparison must start after the baseline ends.';
  return '';
}

/**
 * *Analyze chat consistency*: four steps over one subject. Step 1 sets the periods, from a preset or
 * by hand, and shows Protocol V1 with its overrides; step 2 chooses the runs and the controls, offers
 * the common-grader re-grade and previews what the analysis will see; step 3 shows the saved result;
 * step 4 writes its reports. Focus moves to the step's heading on every step change.
 */
@Component({
  selector: 'app-cc-analysis-wizard',
  standalone: true,
  imports: [CcRegradePanelComponent, CcResultsViewComponent, CcReportsStepComponent],
  templateUrl: './analysis-wizard.component.html',
  styleUrls: ['./analysis-wizard.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcAnalysisWizardComponent implements OnChanges, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() axis: CcModelAxis | null = null;
  @Input() timeline: CcTimeline | null = null;
  @Input() rows: readonly CcRunRow[] = [];
  @Input() analyses: readonly CcAnalysisSummary[] = [];
  /** The benchmark-capable configurations the launcher offers: the re-grade's assessors and the report writers. */
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() pickerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;
  @Input() anchorBusy: ReadonlySet<number> = new Set<number>();

  @Output() readonly analysisSaved = new EventEmitter<CcAnalysisResult>();
  @Output() readonly runsChanged = new EventEmitter<void>();
  @Output() readonly anchorToggle = new EventEmitter<CcRunRow>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly openDocuments = new EventEmitter<CcOpenDocumentsRequest>();

  @ViewChild('stepHeading') stepHeading?: ElementRef<HTMLElement>;

  readonly steps = CC_WIZARD_STEPS;
  readonly protocolEndpoints = CC_PROTOCOL_V1_ENDPOINTS;
  readonly protocol = CC_PROTOCOL_V1;
  readonly periods = ['baseline', 'comparison'] as const;

  step: CcWizardStep = 1;
  name = '';
  days: CcPeriodDays = { baselineStart: '', baselineEnd: '', comparisonStart: '', comparisonEnd: '' };
  preset: CcPreset = 'custom';
  presetAnnotationId: number | null = null;
  presetEventIndex: number | null = null;
  presetNote = '';

  /** The margin overrides as typed, by endpoint id: index points for P1, percent for the others. */
  marginOverrides: Record<string, string> = {};
  alphaOverride = '';

  readonly baselineSelected = new Set<number>();
  readonly comparisonSelected = new Set<number>();
  readonly controlSelected = new Set<number>();
  relaxedPooling = false;
  private selectionKey: string | null = null;

  analyzing = false;
  analyzeError: string | null = null;
  result: CcAnalysisResult | null = null;

  private analyzeSub: Subscription | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    const axisChange = changes['axis'];
    if (axisChange && axisChange.previousValue?.key !== this.axis?.key) {
      // A saved result just opened for this subject is kept; any other subject starts over.
      if (!(this.result && this.axis && this.result.subject.key === this.axis.key)) {
        this.resetForSubject();
      }
    }
  }

  ngOnDestroy(): void {
    this.analyzeSub?.unsubscribe();
  }

  // --- Navigation ---

  get periodsError(): string {
    return periodsRefusal(this.days);
  }

  get overridesError(): string {
    for (const endpoint of CC_PROTOCOL_V1_ENDPOINTS) {
      const text = (this.marginOverrides[endpoint.id] ?? '').trim();
      if (text === '') continue;
      const value = Number(text);
      if (!Number.isFinite(value) || value <= 0) return `The margin of ${endpoint.id} must be a positive number.`;
    }
    const alpha = this.alphaOverride.trim();
    if (alpha !== '') {
      const value = Number(alpha);
      if (!Number.isFinite(value) || value <= 0 || value >= 0.5) return 'α must lie strictly between 0 and 0.5.';
    }
    return '';
  }

  reachable(step: CcWizardStep): boolean {
    switch (step) {
      case 1: return true;
      case 2: return !!this.axis && !this.periodsError && !this.overridesError;
      default: return this.result !== null;
    }
  }

  goTo(step: CcWizardStep): void {
    if (step === this.step || !this.reachable(step)) return;
    if (step === 2) this.preselect();
    this.step = step;
    this.focusHeading();
  }

  next(): void {
    if (this.step < 4) this.goTo((this.step + 1) as CcWizardStep);
  }

  back(): void {
    if (this.step > 1) this.goTo((this.step - 1) as CcWizardStep);
  }

  /** Opens a saved analysis on its results. */
  showResult(result: CcAnalysisResult): void {
    this.analyzeSub?.unsubscribe();
    this.analyzing = false;
    this.analyzeError = null;
    this.result = result;
    this.name = result.name ?? '';
    this.days = {
      baselineStart: formatUtcDate(result.baseline.startUtc),
      baselineEnd: formatUtcDate(result.baseline.endUtc),
      comparisonStart: formatUtcDate(result.comparison.startUtc),
      comparisonEnd: formatUtcDate(result.comparison.endUtc)
    };
    this.preset = 'custom';
    this.presetNote = '';
    this.relaxedPooling = result.endpoints.some(endpoint => endpoint.relaxedPooling);
    this.replaceSelection(this.baselineSelected, result.baseline.runIds);
    this.replaceSelection(this.comparisonSelected, result.comparison.runIds);
    this.replaceSelection(this.controlSelected, result.controls.controlRunIds);
    this.selectionKey = this.periodKey;
    this.step = 3;
    this.focusHeading();
  }

  private focusHeading(): void {
    this.cdr.detectChanges();
    this.stepHeading?.nativeElement.focus();
  }

  // --- Step 1: subject and periods ---

  /** The annotations and events the before-and-after presets list. */
  get annotations(): readonly CcAnnotation[] {
    return this.timeline?.annotations ?? [];
  }

  get events(): readonly CcEvent[] {
    return this.timeline?.events ?? [];
  }

  /** The most recent saved analysis of this subject: the last look *Confirm on later data* starts after. */
  get lastAnalysis(): CcAnalysisSummary | null {
    const key = this.axis?.key;
    if (!key) return null;
    return [...this.analyses]
      .filter(analysis => analysis.subjectModelKey === key)
      .sort((a, b) => utcMillis(b.createdAtUtc) - utcMillis(a.createdAtUtc))[0] ?? null;
  }

  choosePreset(preset: CcPreset): void {
    this.preset = preset;
    this.presetNote = '';
    const axis = this.axis;
    if (!axis) return;
    switch (preset) {
      case 'launch':
        this.days = launchPreset(axis);
        break;
      case 'annotation': {
        const annotation = this.annotations.find(a => a.id === this.presetAnnotationId) ?? this.annotations[0];
        if (!annotation) {
          this.presetNote = 'This model has no annotation in the timeline range.';
          break;
        }
        this.presetAnnotationId = annotation.id;
        this.days = aroundPreset(axis, annotation.atUtc);
        break;
      }
      case 'event': {
        const index = this.presetEventIndex ?? 0;
        const event = this.events[index];
        if (!event) {
          this.presetNote = 'No Overseer change was detected in the timeline range.';
          break;
        }
        this.presetEventIndex = index;
        this.days = aroundPreset(axis, event.atUtc);
        break;
      }
      case 'later': {
        const last = this.lastAnalysis;
        if (!last) {
          this.presetNote = 'This model has no saved analysis yet; there is no earlier look to confirm.';
          break;
        }
        this.days = laterDataPreset(axis, last);
        this.presetNote = `Compares the runs after the last analysis, saved ${formatUtcDateTime(last.createdAtUtc)}, with its baseline.`;
        break;
      }
    }
    this.cdr.markForCheck();
  }

  onAnnotationPick(event: Event): void {
    this.presetAnnotationId = Number((event.target as HTMLSelectElement).value);
    this.choosePreset('annotation');
  }

  onEventPick(event: Event): void {
    this.presetEventIndex = Number((event.target as HTMLSelectElement).value);
    this.choosePreset('event');
  }

  onDayInput(field: keyof CcPeriodDays, event: Event): void {
    this.days = { ...this.days, [field]: (event.target as HTMLInputElement).value };
    this.preset = 'custom';
    this.presetNote = '';
    this.cdr.markForCheck();
  }

  onNameInput(event: Event): void {
    this.name = (event.target as HTMLInputElement).value;
  }

  marginValue(id: string): string {
    return this.marginOverrides[id] ?? '';
  }

  onMarginInput(id: string, event: Event): void {
    this.marginOverrides = { ...this.marginOverrides, [id]: (event.target as HTMLInputElement).value };
    this.cdr.markForCheck();
  }

  onAlphaInput(event: Event): void {
    this.alphaOverride = (event.target as HTMLInputElement).value;
    this.cdr.markForCheck();
  }

  day(value: string): string {
    return formatUtcDate(value);
  }

  annotationLabel(annotation: CcAnnotation): string {
    return `${formatUtcDate(annotation.atUtc)}: ${annotation.text}`;
  }

  eventLabel(event: CcEvent): string {
    return `${formatUtcDate(event.atUtc)}: ${event.label}`;
  }

  // --- Step 2: runs ---

  private get periodKey(): string {
    const d = this.days;
    return `${this.axis?.key}|${d.baselineStart}|${d.baselineEnd}|${d.comparisonStart}|${d.comparisonEnd}`;
  }

  periodRows(period: 'baseline' | 'comparison'): CcRunRow[] {
    const [start, end] = period === 'baseline'
      ? [this.days.baselineStart, this.days.baselineEnd]
      : [this.days.comparisonStart, this.days.comparisonEnd];
    return [...this.rows]
      .filter(row => withinUtcDays(row.startedAtUtc, start, end))
      .sort((a, b) => utcMillis(a.startedAtUtc) - utcMillis(b.startedAtUtc));
  }

  /** The eligible runs of each period, and every matched control, the first time the periods reach step 2. */
  private preselect(): void {
    const key = this.periodKey;
    if (key === this.selectionKey) return;
    this.selectionKey = key;
    this.replaceSelection(this.baselineSelected, this.periodRows('baseline').filter(isRunEligible).map(row => row.runId));
    this.replaceSelection(this.comparisonSelected, this.periodRows('comparison').filter(isRunEligible).map(row => row.runId));
    this.replaceSelection(this.controlSelected, this.controlCandidates);
  }

  private replaceSelection(set: Set<number>, ids: readonly number[]): void {
    set.clear();
    for (const id of ids) set.add(id);
  }

  selection(period: 'baseline' | 'comparison'): Set<number> {
    return period === 'baseline' ? this.baselineSelected : this.comparisonSelected;
  }

  toggleRun(period: 'baseline' | 'comparison', runId: number, event: Event): void {
    const set = this.selection(period);
    if ((event.target as HTMLInputElement).checked) set.add(runId); else set.delete(runId);
    this.cdr.markForCheck();
  }

  toggleControl(runId: number, event: Event): void {
    if ((event.target as HTMLInputElement).checked) this.controlSelected.add(runId); else this.controlSelected.delete(runId);
    this.cdr.markForCheck();
  }

  onRelaxedPooling(event: Event): void {
    this.relaxedPooling = (event.target as HTMLInputElement).checked;
    this.cdr.markForCheck();
  }

  /** The control runs matched to any run of either period, ascending. */
  get controlCandidates(): number[] {
    const ids = new Set<number>();
    for (const row of [...this.periodRows('baseline'), ...this.periodRows('comparison')]) {
      for (const id of row.matchedControlRunIds) ids.add(id);
    }
    return [...ids].sort((a, b) => a - b);
  }

  /** The runs a re-grade covers: the selected target runs and the selected controls. */
  get regradeRunIds(): number[] {
    return [...new Set([...this.baselineSelected, ...this.comparisonSelected, ...this.controlSelected])].sort((a, b) => a - b);
  }

  private pointsOf(ids: ReadonlySet<number>): CcTimelinePoint[] {
    return (this.timeline?.points ?? []).filter(point => ids.has(point.runId));
  }

  /** The time strata both periods' selected runs sampled. */
  get commonStrata(): string[] {
    const strataOf = (ids: ReadonlySet<number>) => new Set(this.pointsOf(ids).flatMap(point => point.strata));
    const baseline = strataOf(this.baselineSelected);
    const comparison = strataOf(this.comparisonSelected);
    return [...baseline].filter(stratum => comparison.has(stratum)).sort();
  }

  /** The Overseer changes detected between the baseline's start and the comparison's end. */
  get eventsInSpan(): CcEvent[] {
    return this.events.filter(event => withinUtcDays(event.atUtc, this.days.baselineStart, this.days.comparisonEnd));
  }

  /** Selected target runs without a matched control run. */
  get missingControls(): CcRunRow[] {
    const selected = new Set([...this.baselineSelected, ...this.comparisonSelected]);
    return [...this.periodRows('baseline'), ...this.periodRows('comparison')]
      .filter(row => selected.has(row.runId) && row.matchedControlRunIds.length === 0);
  }

  get analyzeBlocked(): string {
    if (!this.axis) return 'Choose a model in the Timeline first.';
    if (this.periodsError) return this.periodsError;
    if (this.overridesError) return this.overridesError;
    if (this.baselineSelected.size === 0) return 'Select at least one baseline run.';
    if (this.comparisonSelected.size === 0) return 'Select at least one comparison run.';
    return '';
  }

  started(row: CcRunRow): string {
    return formatUtcDateTime(row.startedAtUtc);
  }

  controls(row: CcRunRow): string {
    return controlRunsText(row);
  }

  eligible(row: CcRunRow): boolean {
    return isRunEligible(row);
  }

  /** The request step 2's Analyze sends. */
  buildRequest(): CcAnalysisRequest | null {
    const axis = this.axis;
    if (!axis || this.analyzeBlocked) return null;
    const request: CcAnalysisRequest = {
      subjectModelKey: axis.key,
      baselineStartUtc: startOfUtcDay(this.days.baselineStart)!,
      baselineEndUtc: endOfUtcDay(this.days.baselineEnd)!,
      comparisonStartUtc: startOfUtcDay(this.days.comparisonStart)!,
      comparisonEndUtc: endOfUtcDay(this.days.comparisonEnd)!,
      baselineRunIds: [...this.baselineSelected].sort((a, b) => a - b),
      comparisonRunIds: [...this.comparisonSelected].sort((a, b) => a - b),
      relaxedPooling: this.relaxedPooling
    };
    const name = this.name.trim();
    if (name) request.name = name;
    // Without matched candidates the server chooses the controls itself.
    if (this.controlCandidates.length > 0) request.controlRunIds = [...this.controlSelected].sort((a, b) => a - b);
    const overrides = this.protocolOverrides();
    if (overrides) request.protocolOverrides = overrides;
    return request;
  }

  private protocolOverrides(): CcProtocolOverrides | null {
    const margins: Record<string, number> = {};
    for (const endpoint of CC_PROTOCOL_V1_ENDPOINTS) {
      const text = (this.marginOverrides[endpoint.id] ?? '').trim();
      if (text === '') continue;
      const value = Number(text);
      margins[endpoint.id] = endpoint.unit === '%' ? value / 100 : value;
    }
    const overrides: CcProtocolOverrides = {};
    if (Object.keys(margins).length > 0) overrides.margins = margins;
    if (this.alphaOverride.trim() !== '') overrides.alpha = Number(this.alphaOverride.trim());
    return Object.keys(overrides).length > 0 ? overrides : null;
  }

  analyze(): void {
    const request = this.buildRequest();
    if (!request || this.analyzing) return;
    this.analyzing = true;
    this.analyzeError = null;
    this.cdr.markForCheck();
    this.analyzeSub?.unsubscribe();
    this.analyzeSub = this.service.analyze(request).subscribe({
      next: result => {
        this.analyzing = false;
        this.result = result;
        this.step = 3;
        this.analysisSaved.emit(result);
        this.focusHeading();
      },
      error: err => {
        this.analyzing = false;
        this.analyzeError = ccErrorText(err, 'The analysis could not be run.');
        this.cdr.markForCheck();
      }
    });
  }

  /** Abandons the request in flight; the server stops on the client's abort. */
  stopAnalyze(): void {
    this.analyzeSub?.unsubscribe();
    this.analyzeSub = null;
    this.analyzing = false;
    this.cdr.markForCheck();
  }

  onRegradeFinished(): void {
    this.runsChanged.emit();
  }

  // --- Reset ---

  private resetForSubject(): void {
    this.analyzeSub?.unsubscribe();
    this.analyzing = false;
    this.analyzeError = null;
    this.result = null;
    this.step = 1;
    this.name = '';
    this.presetAnnotationId = null;
    this.presetEventIndex = null;
    this.marginOverrides = {};
    this.alphaOverride = '';
    this.relaxedPooling = false;
    this.baselineSelected.clear();
    this.comparisonSelected.clear();
    this.controlSelected.clear();
    this.selectionKey = null;
    this.days = { baselineStart: '', baselineEnd: '', comparisonStart: '', comparisonEnd: '' };
    if (this.axis) {
      this.choosePreset('launch');
    } else {
      this.preset = 'custom';
      this.presetNote = '';
    }
  }
}
