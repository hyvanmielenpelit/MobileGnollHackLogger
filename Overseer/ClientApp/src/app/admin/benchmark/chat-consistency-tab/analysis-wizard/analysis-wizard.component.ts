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
  ViewChild,
  inject
} from '@angular/core';
import { Subscription } from 'rxjs';

import { SystemAiConfigDto } from '../../../../services/admin.service';
import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { DateFieldComponent } from '../../../../shared/date-field/date-field.component';
import { ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import {
  addUtcDays,
  controlRunsText,
  endOfUtcDay,
  formatUtcDate,
  formatUtcDateTime,
  isRunEligible,
  isUtcDateInput,
  plural,
  startOfUtcDay,
  utcMillis,
  withinUtcDays
} from '../chat-consistency-format';
import { CC_HARNESS_EVENT_KIND, CcEventGroup, groupOverseerEvents } from '../chat-consistency-events';
import { CC_ALL_DATES, CcDateRange, ccDateRangeText, ccRangeBounds } from '../chat-consistency-range';
import { CC_EMPTY_SCOPE, CcRunScope, scopeIsDefault } from '../chat-consistency-scope';
import {
  CcAnalysisRequest,
  CcAnalysisResult,
  CcAnalysisSummary,
  CcAnnotation,
  CcModelAxis,
  CcOpenDocumentsRequest,
  CcProtocolOverrides,
  CcRunRow,
  CcRunSelection,
  CcTimeline,
  CcTimelinePoint
} from '../chat-consistency.models';
import { CcRegradePanelComponent } from './regrade-panel.component';
import { CcReportsStepComponent } from './reports-step.component';
import { CcResultsViewComponent } from './results-view.component';

/** The analysis steps the outer wizard shows through this component, in order. */
export type CcAnalysisStep = 'periods' | 'runs' | 'results' | 'reports';

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

/** The UTC days of the first and last run the analysis may use. */
export interface CcRunSpan {
  first: string;
  last: string;
}

/** The span of the runs chosen in step 1 when given, else the subject's first and last run day. */
export function seriesDays(axis: CcModelAxis, span: CcRunSpan | null = null): CcRunSpan {
  return span ?? { first: formatUtcDate(axis.firstRunAtUtc), last: formatUtcDate(axis.lastRunAtUtc) };
}

function sameSpan(a: CcRunSpan | null | undefined, b: CcRunSpan | null | undefined): boolean {
  return (a ?? null) === (b ?? null) || (!!a && !!b && a.first === b.first && a.last === b.last);
}

function maxDay(a: string, b: string): string {
  return a > b ? a : b;
}

function minDay(a: string, b: string): string {
  return a < b ? a : b;
}

/** *Launch vs last 14 days*: the first 14 days of the series against its last 14, never overlapping. */
export function launchPreset(axis: CcModelAxis, span: CcRunSpan | null = null): CcPeriodDays {
  const { first, last } = seriesDays(axis, span);
  const comparisonStart = maxDay(addUtcDays(last, -13), first);
  return {
    baselineStart: first,
    baselineEnd: minDay(addUtcDays(first, 13), addUtcDays(comparisonStart, -1)),
    comparisonStart,
    comparisonEnd: last
  };
}

/** *Before vs after*: up to {@link CC_PRESET_WINDOW_DAYS} days before the day of `atUtc` against that day and the days after. */
export function aroundPreset(axis: CcModelAxis, atUtc: string, span: CcRunSpan | null = null): CcPeriodDays {
  const { first, last } = seriesDays(axis, span);
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
 * from the day after it was saved to the series' last run.
 */
export function laterDataPreset(axis: CcModelAxis, last: CcAnalysisSummary, span: CcRunSpan | null = null): CcPeriodDays {
  return {
    baselineStart: formatUtcDate(last.baselineStartUtc),
    baselineEnd: formatUtcDate(last.baselineEndUtc),
    comparisonStart: addUtcDays(formatUtcDate(last.createdAtUtc), 1),
    comparisonEnd: seriesDays(axis, span).last
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
 * The analysis steps of the Chat Consistency wizard over one subject, without navigation of their
 * own: the outer wizard chooses the step through `step` and draws the step bar, headings and footer.
 * *Periods* sets the periods, from a preset or by hand, and shows Protocol V1 with its overrides;
 * *Runs and controls* chooses the runs and the controls, offers the common-grader re-grade and
 * previews what the analysis will see; *Results* shows the saved result; *Reports* writes its
 * reports. A step's body is mounted on its first visit and afterwards kept, hidden while another
 * step shows, so a re-grade, a chart attachment, the charts and the scroll survive a step change.
 */
@Component({
  selector: 'app-cc-analysis-wizard',
  standalone: true,
  imports: [DateFieldComponent, CcRegradePanelComponent, CcResultsViewComponent, CcReportsStepComponent],
  templateUrl: './analysis-wizard.component.html',
  styleUrls: ['./analysis-wizard.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcAnalysisWizardComponent implements OnChanges, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() axis: CcModelAxis | null = null;
  @Input() timeline: CcTimeline | null = null;
  /** The runs in the analysis: the step-1 dates narrowed by the step-1 selection. */
  @Input() rows: readonly CcRunRow[] = [];
  /** Every run in the step-1 dates, those left out included. */
  @Input() allRows: readonly CcRunRow[] = [];
  /** The step-1 selection, recorded with the analysis. */
  @Input() scope: CcRunScope = CC_EMPTY_SCOPE;
  /** The step-1 dates, recorded with the analysis. */
  @Input() range: CcDateRange = CC_ALL_DATES;
  /** The days of the first and last run in the analysis, which the presets span; null with none. */
  @Input() span: CcRunSpan | null = null;
  /** Changes with the step-1 selection, so a new selection preselects the runs again. */
  @Input() scopeKey = '';
  @Input() analyses: readonly CcAnalysisSummary[] = [];
  /** The benchmark-capable configurations the launcher offers: the re-grade's assessors and the report writers. */
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() pickerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;
  @Input() anchorBusy: ReadonlySet<number> = new Set<number>();
  /** The step whose body shows. */
  @Input() step: CcAnalysisStep = 'periods';

  @Output() readonly analysisSaved = new EventEmitter<CcAnalysisResult>();
  @Output() readonly runsChanged = new EventEmitter<void>();
  @Output() readonly anchorToggle = new EventEmitter<CcRunRow>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly openDocuments = new EventEmitter<CcOpenDocumentsRequest>();
  /** The state the outer wizard reads (errors, blocked reason, analyzing, result) may have changed. */
  @Output() readonly stateChange = new EventEmitter<void>();

  @ViewChild(CcReportsStepComponent) reportsStep?: CcReportsStepComponent;

  readonly protocolEndpoints = CC_PROTOCOL_V1_ENDPOINTS;
  readonly protocol = CC_PROTOCOL_V1;
  readonly periods = ['baseline', 'comparison'] as const;

  name = '';
  days: CcPeriodDays = { baselineStart: '', baselineEnd: '', comparisonStart: '', comparisonEnd: '' };
  preset: CcPreset = 'custom';
  presetAnnotationId: number | null = null;
  /** The key of the composite event the *Before vs after an Overseer change* preset is around. */
  presetEventGroupKey: string | null = null;
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
  /** The steps whose bodies have been shown, and so stay mounted. */
  private readonly visitedSteps = new Set<CcAnalysisStep>();
  private groupsCache: { timeline: CcTimeline | null; groups: CcEventGroup[] } | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['step']) this.visitedSteps.add(this.step);
    const axisChange = changes['axis'];
    let reset = false;
    if (axisChange && axisChange.previousValue?.key !== this.axis?.key) {
      // A saved result just opened for this subject is kept; any other subject starts over.
      if (!(this.result && this.axis && this.result.subject.key === this.axis.key)) {
        this.resetForSubject();
        reset = true;
      }
    }
    // A preset follows the runs in the analysis; dates typed by hand stay.
    const spanChange = changes['span'];
    if (spanChange && !reset && !sameSpan(spanChange.previousValue, this.span) && this.preset !== 'custom') {
      this.choosePreset(this.preset);
    }
    if ((changes['rows'] || changes['scopeKey']) && this.step === 'runs') this.preselectRuns();
  }

  ngOnDestroy(): void {
    this.analyzeSub?.unsubscribe();
  }

  // --- Steps ---

  /** A step's body is rendered while it shows and, once shown, kept mounted and hidden. */
  isMounted(step: CcAnalysisStep): boolean {
    return step === this.step || this.visitedSteps.has(step);
  }

  /** The Reports step is drawing and uploading report charts; closing the wizard would strand them. */
  get chartsAttaching(): boolean {
    return this.reportsStep?.chartState === 'attaching';
  }

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

  reachable(step: CcAnalysisStep): boolean {
    switch (step) {
      case 'periods': return true;
      case 'runs': return !!this.axis && !this.periodsError && !this.overridesError;
      default: return this.result !== null;
    }
  }

  /** Loads a saved analysis, for the outer wizard to show on its results. */
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
    this.changed();
  }

  /** Marks the view for check and tells the outer wizard its state may have changed. */
  private changed(): void {
    this.cdr.markForCheck();
    this.stateChange.emit();
  }

  // --- Periods ---

  /** The annotations the *Before vs after an annotation* preset lists. */
  get annotations(): readonly CcAnnotation[] {
    return this.timeline?.annotations ?? [];
  }

  /** The timeline's Overseer events grouped into composite events, oldest first; recomputed per timeline. */
  get eventGroups(): readonly CcEventGroup[] {
    const timeline = this.timeline;
    let cache = this.groupsCache;
    if (!cache || cache.timeline !== timeline) {
      cache = { timeline, groups: groupOverseerEvents(timeline?.events ?? [], timeline?.points ?? []) };
      this.groupsCache = cache;
    }
    return cache.groups;
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
        this.days = launchPreset(axis, this.span);
        break;
      case 'annotation': {
        const annotation = this.annotations.find(a => a.id === this.presetAnnotationId) ?? this.annotations[0];
        if (!annotation) {
          this.presetNote = 'This model has no annotation in the timeline range.';
          break;
        }
        this.presetAnnotationId = annotation.id;
        this.days = aroundPreset(axis, annotation.atUtc, this.span);
        break;
      }
      case 'event': {
        const groups = this.eventGroups;
        const group = groups.find(g => g.key === this.presetEventGroupKey) ?? groups[0];
        if (!group) {
          this.presetNote = 'No Overseer change was detected in the timeline range.';
          break;
        }
        this.presetEventGroupKey = group.key;
        this.days = aroundPreset(axis, group.atUtc, this.span);
        break;
      }
      case 'later': {
        const last = this.lastAnalysis;
        if (!last) {
          this.presetNote = 'This model has no saved analysis yet; there is no earlier look to confirm.';
          break;
        }
        this.days = laterDataPreset(axis, last, this.span);
        this.presetNote = `Compares the runs after the last analysis, saved ${formatUtcDateTime(last.createdAtUtc)}, with its baseline.`;
        break;
      }
    }
    this.changed();
  }

  onAnnotationPick(event: Event): void {
    this.presetAnnotationId = Number((event.target as HTMLSelectElement).value);
    this.choosePreset('annotation');
  }

  onEventPick(event: Event): void {
    this.presetEventGroupKey = (event.target as HTMLSelectElement).value;
    this.choosePreset('event');
  }

  /**
   * Which runs the presets span: `Presets use the runs chosen in step 1: #21 (2026-09-20) to #93
   * (2026-10-05), 15 runs.`, or every run in the dates while step 1 leaves the selection alone.
   */
  get spanNote(): string {
    if (this.rows.length === 0) return '';
    const ordered = [...this.rows].sort((a, b) => utcMillis(a.startedAtUtc) - utcMillis(b.startedAtUtc) || a.runId - b.runId);
    const first = ordered[0];
    const last = ordered[ordered.length - 1];
    const runs = first === last
      ? `#${first.runId} (${formatUtcDate(first.startedAtUtc)}), 1 run`
      : `#${first.runId} (${formatUtcDate(first.startedAtUtc)}) to #${last.runId} (${formatUtcDate(last.startedAtUtc)}), ${plural(ordered.length, 'run')}`;
    return scopeIsDefault(this.scope)
      ? `Presets use every run in the dates: ${runs}.`
      : `Presets use the runs chosen in step 1: ${runs}.`;
  }

  onDayInput(field: keyof CcPeriodDays, value: string): void {
    this.days = { ...this.days, [field]: value };
    this.preset = 'custom';
    this.presetNote = '';
    this.changed();
  }

  onNameInput(event: Event): void {
    this.name = (event.target as HTMLInputElement).value;
  }

  marginValue(id: string): string {
    return this.marginOverrides[id] ?? '';
  }

  onMarginInput(id: string, event: Event): void {
    this.marginOverrides = { ...this.marginOverrides, [id]: (event.target as HTMLInputElement).value };
    this.changed();
  }

  onAlphaInput(event: Event): void {
    this.alphaOverride = (event.target as HTMLInputElement).value;
    this.changed();
  }

  day(value: string): string {
    return formatUtcDate(value);
  }

  annotationLabel(annotation: CcAnnotation): string {
    return `${formatUtcDate(annotation.atUtc)}: ${annotation.text}`;
  }

  /** A composite event as the preset lists it: `E2 · 2026-10-04 · Harness 27 → 28 (5 changes)`, counting its kinds. */
  eventGroupOption(group: CcEventGroup): string {
    return `${group.tag} · ${group.day} · ${group.title} (${plural(group.changes.length, 'change')})`;
  }

  /** A composite event's kinds as the preview lists them, `System prompt ×3, Tool guides`; the harness is left out when the title names it. */
  eventGroupChanges(group: CcEventGroup): string {
    return group.changes
      .filter(change => !(group.harnessChange && change.kind === CC_HARNESS_EVENT_KIND))
      .map(change => change.count > 1 ? `${change.label} ×${change.count}` : change.label)
      .join(', ');
  }

  // --- Runs and controls ---

  private get periodKey(): string {
    const d = this.days;
    return `${this.axis?.key}|${d.baselineStart}|${d.baselineEnd}|${d.comparisonStart}|${d.comparisonEnd}|${this.scopeKey}`;
  }

  /** The runs left out in step 1 that fall inside either period, ascending. */
  get leftOutInPeriods(): number[] {
    if (this.scope.leftOut.size === 0) return [];
    const d = this.days;
    return this.allRows
      .filter(row => this.scope.leftOut.has(row.runId)
        && (withinUtcDays(row.startedAtUtc, d.baselineStart, d.baselineEnd)
          || withinUtcDays(row.startedAtUtc, d.comparisonStart, d.comparisonEnd)))
      .map(row => row.runId)
      .sort((a, b) => a - b);
  }

  /** `#45, #51`. */
  runList(ids: readonly number[]): string {
    return ids.map(id => `#${id}`).join(', ');
  }

  periodRows(period: 'baseline' | 'comparison'): CcRunRow[] {
    const [start, end] = period === 'baseline'
      ? [this.days.baselineStart, this.days.baselineEnd]
      : [this.days.comparisonStart, this.days.comparisonEnd];
    return [...this.rows]
      .filter(row => withinUtcDays(row.startedAtUtc, start, end))
      .sort((a, b) => utcMillis(a.startedAtUtc) - utcMillis(b.startedAtUtc));
  }

  /**
   * Selects the eligible runs of each period and every matched control, the first time these periods
   * reach *Runs and controls*; the outer wizard calls it on entering that step.
   */
  preselectRuns(): void {
    const key = this.periodKey;
    if (key === this.selectionKey) return;
    // Runs still loading: chosen when they arrive (ngOnChanges).
    if (this.rows.length === 0) return;
    this.selectionKey = key;
    this.replaceSelection(this.baselineSelected, this.periodRows('baseline').filter(isRunEligible).map(row => row.runId));
    this.replaceSelection(this.comparisonSelected, this.periodRows('comparison').filter(isRunEligible).map(row => row.runId));
    this.replaceSelection(this.controlSelected, this.controlCandidates);
    this.changed();
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
    this.changed();
  }

  toggleControl(runId: number, event: Event): void {
    if ((event.target as HTMLInputElement).checked) this.controlSelected.add(runId); else this.controlSelected.delete(runId);
    this.changed();
  }

  onRelaxedPooling(event: Event): void {
    this.relaxedPooling = (event.target as HTMLInputElement).checked;
    this.changed();
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

  /** The composite Overseer events between the baseline's start and the comparison's end. */
  get eventGroupsInSpan(): CcEventGroup[] {
    return this.eventGroups.filter(group => withinUtcDays(group.atUtc, this.days.baselineStart, this.days.comparisonEnd));
  }

  /** Selected target runs without a matched control run. */
  get missingControls(): CcRunRow[] {
    const selected = new Set([...this.baselineSelected, ...this.comparisonSelected]);
    return [...this.periodRows('baseline'), ...this.periodRows('comparison')]
      .filter(row => selected.has(row.runId) && row.matchedControlRunIds.length === 0);
  }

  get analyzeBlocked(): string {
    if (!this.axis) return 'Choose a model in step 1 first.';
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

  /** The request Analyze sends. */
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
      relaxedPooling: this.relaxedPooling,
      runSelection: this.runSelection()
    };
    const name = this.name.trim();
    if (name) request.name = name;
    // Without matched candidates the server chooses the controls itself.
    if (this.controlCandidates.length > 0) request.controlRunIds = [...this.controlSelected].sort((a, b) => a - b);
    const overrides = this.protocolOverrides();
    if (overrides) request.protocolOverrides = overrides;
    return request;
  }

  /** The step-1 selection as the analysis records it; sent with every request, the default one included. */
  private runSelection(): CcRunSelection {
    const bounds = ccRangeBounds(this.range);
    return {
      rangeLabel: ccDateRangeText(this.range),
      rangeFromUtc: bounds.fromUtc,
      rangeToUtc: bounds.toUtc,
      firstRunId: this.scope.firstRunId,
      lastRunId: this.scope.lastRunId,
      leftOutRunIds: [...this.scope.leftOut].sort((a, b) => a - b)
    };
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
    this.changed();
    this.analyzeSub?.unsubscribe();
    this.analyzeSub = this.service.analyze(request).subscribe({
      next: result => {
        this.analyzing = false;
        this.result = result;
        this.changed();
        // The outer wizard moves to Results on this.
        this.analysisSaved.emit(result);
      },
      error: err => {
        this.analyzing = false;
        this.analyzeError = ccErrorText(err, 'The analysis could not be run.');
        this.changed();
      }
    });
  }

  /** Abandons the request in flight; the server stops on the client's abort. */
  stopAnalyze(): void {
    this.analyzeSub?.unsubscribe();
    this.analyzeSub = null;
    this.analyzing = false;
    this.changed();
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
    this.name = '';
    this.presetAnnotationId = null;
    this.presetEventGroupKey = null;
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
      this.changed();
    }
  }
}
