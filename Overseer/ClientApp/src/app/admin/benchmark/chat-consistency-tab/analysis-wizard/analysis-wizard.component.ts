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
import { ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import {
  controlRunsText,
  formatUtcDate,
  formatUtcDateTime,
  plural,
  utcMillis
} from '../chat-consistency-format';
import { CC_HARNESS_EVENT_KIND, CcEventGroup, groupOverseerEvents } from '../chat-consistency-events';
import {
  CC_NO_PERIOD_IDS,
  CcPeriod,
  CcPeriodIds,
  CcPeriodUnit,
  CcPeriodWindows,
  CcPresetOutcome,
  CcUnitPeriod,
  ccBeforeAfter,
  ccConfirmOnLaterData,
  ccEarliestVsLatest,
  ccIdsFromUnits,
  ccPeriodAssignment,
  ccPeriodUnits,
  ccPeriodWindows,
  ccPeriodsRefusal,
  ccPruneIds,
  sameIds
} from '../chat-consistency-periods';
import { CC_ALL_DATES, CcDateRange, ccDateRangeText, ccRangeBounds } from '../chat-consistency-range';
import { CC_EMPTY_SCOPE, CcRunScope, scopeIsDefault } from '../chat-consistency-scope';
import {
  CcAnalysisRequest,
  CcAnalysisResult,
  CcAnalysisSummary,
  CcAnnotation,
  CcBatteryRunRow,
  CcComparisonSet,
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
export type CcAnalysisStep = 'analyze' | 'results';

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

export type CcPreset = 'earliest' | 'annotation' | 'event' | 'later' | 'custom';

/** One of the four run choices. */
export type CcRunChoice = keyof CcPeriodIds;

const UNIT_PERIOD_TEXT: Readonly<Record<CcUnitPeriod, string>> = {
  baseline: 'Baseline',
  comparison: 'Comparison',
  notUsed: 'Not used',
  notEligible: 'Not eligible'
};

/** A saved analysis's units, which name the run choices once they are among the step-1 units. */
interface ResultUnits {
  battery: boolean;
  baseline: readonly number[];
  comparison: readonly number[];
}

/** What the run choices derive, kept until the units, the choices or the anchor change. */
interface PeriodState {
  units: readonly CcPeriodUnit[];
  ids: CcPeriodIds;
  anchorUtc: string | null;
  battery: boolean;
  refusal: string;
  assignment: ReadonlyMap<number, CcUnitPeriod>;
  /** The eligible units of each period; empty while the choices are refused. */
  baseline: readonly CcPeriodUnit[];
  comparison: readonly CcPeriodUnit[];
  windows: CcPeriodWindows | null;
}

/**
 * The analysis steps of the Chat Consistency wizard over one subject, without navigation of their
 * own: the outer wizard chooses the step through `step` and draws the step bar, headings and footer.
 * *Analyze* splits the step-1 units into a baseline and a comparison by four run choices, from a
 * preset or by hand, chooses the controls, offers the common-grader re-grade, shows Protocol V1 with
 * its overrides and previews what the analysis will see; *Results* shows the saved result and its
 * reports. A step's body is mounted on its first visit and afterwards kept, hidden while another step
 * shows, so a re-grade, a chart attachment, the charts and the scroll survive a step change.
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
  /**
   * The runs in the analysis: the step-1 dates narrowed by the step-1 selection; in a battery set, the
   * members of the battery runs in the analysis.
   */
  @Input() rows: readonly CcRunRow[] = [];
  /** Every run of the compared set in the step-1 dates, those left out included. */
  @Input() allRows: readonly CcRunRow[] = [];
  /** The battery or suite compared within; null analyzes the runs one by one. */
  @Input() compareSet: CcComparisonSet | null = null;
  /** In a battery set, the battery runs in the analysis, oldest first. */
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  /** In a battery set, every battery run of the set in the step-1 dates. */
  @Input() allBatteryRows: readonly CcBatteryRunRow[] = [];
  /** The step-1 selection, recorded with the analysis: battery run ids in a battery set. */
  @Input() scope: CcRunScope = CC_EMPTY_SCOPE;
  /** The step-1 dates, recorded with the analysis. */
  @Input() range: CcDateRange = CC_ALL_DATES;
  /** Changes with the step-1 selection, so a new selection chooses the controls again. */
  @Input() scopeKey = '';
  @Input() analyses: readonly CcAnalysisSummary[] = [];
  /** The benchmark-capable configurations the launcher offers: the re-grade's assessors and the report writers. */
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() pickerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;
  /** The step whose body shows. */
  @Input() step: CcAnalysisStep = 'analyze';

  @Output() readonly analysisSaved = new EventEmitter<CcAnalysisResult>();
  @Output() readonly runsChanged = new EventEmitter<void>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly openDocuments = new EventEmitter<CcOpenDocumentsRequest>();
  /** The state the outer wizard reads (errors, blocked reason, analyzing, result) may have changed. */
  @Output() readonly stateChange = new EventEmitter<void>();

  @ViewChild(CcReportsStepComponent) reportsStep?: CcReportsStepComponent;

  readonly protocolEndpoints = CC_PROTOCOL_V1_ENDPOINTS;
  readonly protocol = CC_PROTOCOL_V1;
  readonly periods: readonly CcPeriod[] = ['baseline', 'comparison'];
  /** The two rows of run choices: each period's first and last run select. */
  readonly choiceRows: readonly {
    readonly period: CcPeriod;
    readonly name: string;
    readonly choices: readonly { readonly key: CcRunChoice; readonly id: string; readonly label: string }[];
  }[] = [
    {
      period: 'baseline', name: 'Baseline',
      choices: [
        { key: 'baselineFirstId', id: 'cc-wiz-bf', label: 'Baseline first run' },
        { key: 'baselineLastId', id: 'cc-wiz-bl', label: 'Baseline last run' }
      ]
    },
    {
      period: 'comparison', name: 'Comparison',
      choices: [
        { key: 'comparisonFirstId', id: 'cc-wiz-cf', label: 'Comparison first run' },
        { key: 'comparisonLastId', id: 'cc-wiz-cl', label: 'Comparison last run' }
      ]
    }
  ];

  name = '';
  /** The first and last unit of each period, by id. */
  ids: CcPeriodIds = CC_NO_PERIOD_IDS;
  preset: CcPreset = 'custom';
  presetAnnotationId: number | null = null;
  /** The key of the composite event the *Before vs after an Overseer change* preset is around. */
  presetEventGroupKey: string | null = null;
  /** The instant the applied before-and-after preset splits at; null for any other preset. */
  presetAnchorUtc: string | null = null;
  presetNote = '';

  /** The margin overrides as typed, by endpoint id: index points for P1, percent for the others. */
  marginOverrides: Record<string, string> = {};
  alphaOverride = '';

  /** The checked control runs; only those among {@link controlCandidates} are used. */
  readonly controlSelected = new Set<number>();
  relaxedPooling = false;
  private selectionKey: string | null = null;
  /** A saved analysis's units, until they name the run choices or another choice replaces them. */
  private resultUnits: ResultUnits | null = null;

  analyzing = false;
  analyzeError: string | null = null;
  result: CcAnalysisResult | null = null;

  private analyzeSub: Subscription | null = null;
  /** The steps whose bodies have been shown, and so stay mounted. */
  private readonly visitedSteps = new Set<CcAnalysisStep>();
  private groupsCache: { timeline: CcTimeline | null; groups: CcEventGroup[] } | null = null;
  private unitsCache: {
    rows: readonly CcRunRow[]; batteryRows: readonly CcBatteryRunRow[]; battery: boolean; units: CcPeriodUnit[];
  } | null = null;
  private stateCache: PeriodState | null = null;

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
    if (reset) return;
    if (changes['rows'] || changes['batteryRows'] || changes['compareSet'] || changes['scopeKey']) {
      this.followUnits();
    } else if (changes['timeline'] && (this.preset === 'annotation' || this.preset === 'event')) {
      this.choosePreset(this.preset);
    }
  }

  /** A battery set is compared: the units are battery runs. */
  get batteryMode(): boolean {
    return this.compareSet?.kind === 'battery';
  }

  /** `run`, or `battery run` in a battery set. */
  get noun(): string {
    return this.batteryMode ? 'battery run' : 'run';
  }

  ngOnDestroy(): void {
    this.analyzeSub?.unsubscribe();
  }

  // --- Steps ---

  /** A step's body is rendered while it shows and, once shown, kept mounted and hidden. */
  isMounted(step: CcAnalysisStep): boolean {
    return step === this.step || this.visitedSteps.has(step);
  }

  /** The Reports section is drawing and uploading report charts; closing the wizard would strand them. */
  get chartsAttaching(): boolean {
    return this.reportsStep?.chartState === 'attaching';
  }

  get periodsError(): string {
    return this.periodState.refusal;
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
    return step === 'analyze' ? !!this.axis : this.result !== null;
  }

  /**
   * Loads a saved analysis, for the outer wizard to show on its results. Its units name the run
   * choices, under *Custom*, as soon as they are among the step-1 units.
   */
  showResult(result: CcAnalysisResult): void {
    this.analyzeSub?.unsubscribe();
    this.analyzing = false;
    this.analyzeError = null;
    this.result = result;
    this.name = result.name ?? '';
    this.preset = 'custom';
    this.presetNote = '';
    this.presetAnchorUtc = null;
    this.relaxedPooling = result.endpoints.some(endpoint => endpoint.relaxedPooling);
    if (result.unitKind === 'batteryRun' && result.units) {
      const unitsOf = (period: string) => result.units!.filter(unit => unit.period === period).map(unit => unit.unitId);
      this.resultUnits = { battery: true, baseline: unitsOf('baseline'), comparison: unitsOf('comparison') };
    } else {
      this.resultUnits = { battery: false, baseline: result.baseline.runIds, comparison: result.comparison.runIds };
    }
    this.ids = this.idsFromResult(this.resultUnits);
    this.replaceSelection(this.controlSelected, result.controls.controlRunIds);
    this.selectionKey = this.periodKey;
    this.changed();
  }

  /** Marks the view for check and tells the outer wizard its state may have changed. */
  private changed(): void {
    this.cdr.markForCheck();
    this.stateChange.emit();
  }

  // --- Units and periods ---

  /** The step-1 units, ordered by start then id: battery runs in a battery set (complete ones only), else runs. */
  get units(): readonly CcPeriodUnit[] {
    const battery = this.batteryMode;
    const cache = this.unitsCache;
    if (cache && cache.rows === this.rows && cache.batteryRows === this.batteryRows && cache.battery === battery) {
      return cache.units;
    }
    const units = ccPeriodUnits(this.rows, this.batteryRows, battery);
    this.unitsCache = { rows: this.rows, batteryRows: this.batteryRows, battery, units };
    return units;
  }

  private get periodState(): PeriodState {
    const units = this.units;
    const battery = this.batteryMode;
    const cache = this.stateCache;
    if (cache && cache.units === units && cache.ids === this.ids && cache.anchorUtc === this.presetAnchorUtc
      && cache.battery === battery) {
      return cache;
    }
    const refusal = ccPeriodsRefusal(units, this.ids, battery);
    const assignment = ccPeriodAssignment(units, this.ids);
    const state: PeriodState = {
      units,
      ids: this.ids,
      anchorUtc: this.presetAnchorUtc,
      battery,
      refusal,
      assignment,
      baseline: refusal ? [] : units.filter(unit => assignment.get(unit.id) === 'baseline'),
      comparison: refusal ? [] : units.filter(unit => assignment.get(unit.id) === 'comparison'),
      windows: refusal ? null : ccPeriodWindows(units, this.ids, this.presetAnchorUtc)
    };
    this.stateCache = state;
    return state;
  }

  /** The windows the request carries; null while the run choices are refused. */
  get windows(): CcPeriodWindows | null {
    return this.periodState.windows;
  }

  /** The eligible units of a period; empty while the run choices are refused. */
  periodUnits(period: CcPeriod): readonly CcPeriodUnit[] {
    return period === 'baseline' ? this.periodState.baseline : this.periodState.comparison;
  }

  /** `Baseline`, `Comparison`, `Not used` or `Not eligible`. */
  unitPeriodText(unit: CcPeriodUnit): string {
    return UNIT_PERIOD_TEXT[this.unitPeriod(unit)];
  }

  unitPeriod(unit: CcPeriodUnit): CcUnitPeriod {
    return this.periodState.assignment.get(unit.id) ?? 'notUsed';
  }

  /** A run select's option text: `#12 · 2026-10-08 14:05 UTC`, with ` · not eligible` for an ineligible unit. */
  optionText(unit: CcPeriodUnit): string {
    return `#${unit.id} · ${formatUtcDateTime(unit.startedAtUtc)}${unit.eligible ? '' : ' · not eligible'}`;
  }

  /** A run select's accessible option text in a battery set: `Battery run #12 · …`; null for a run, which reads its text. */
  optionLabel(unit: CcPeriodUnit): string | null {
    return this.batteryMode ? `Battery run ${this.optionText(unit)}` : null;
  }

  /**
   * The sample line of a period: `Baseline: 1 battery run on 1 day (2026-10-08). P1, P4 and P5 need at
   * least 2 on 2 days to be Established.`, or `…, which meets the minimum sample for P1, P4 and P5.`
   */
  sampleLine(period: CcPeriod): string {
    const units = this.periodUnits(period);
    const days = [...new Set(units.map(unit => unit.day))].sort();
    const dayText = days.length === 1 ? days[0] : `${days[0]} to ${days[days.length - 1]}`;
    const facts = `${period === 'baseline' ? 'Baseline' : 'Comparison'}: ${plural(units.length, this.noun)} on ${plural(days.length, 'day')} (${dayText})`;
    const p = CC_PROTOCOL_V1;
    return units.length >= p.minimumRunsPerPeriod && days.length >= p.minimumDaysPerPeriod
      ? `${facts}, which meets the minimum sample for P1, P4 and P5.`
      : `${facts}. P1, P4 and P5 need at least ${p.minimumRunsPerPeriod} on ${p.minimumDaysPerPeriod} days to be Established.`;
  }

  /** The run choices of a saved analysis's units among the current units; unset where none is. */
  private idsFromResult(units: ResultUnits): CcPeriodIds {
    if (units.battery !== this.batteryMode) return CC_NO_PERIOD_IDS;
    return ccIdsFromUnits(this.units, units.baseline, units.comparison);
  }

  /**
   * The step-1 units changed: a preset is applied again; *Custom* choices stay while their units do,
   * and a saved analysis's units name them once they arrive.
   */
  private followUnits(): void {
    if (this.preset !== 'custom') {
      this.choosePreset(this.preset);
      return;
    }
    if (this.resultUnits) {
      const ids = this.idsFromResult(this.resultUnits);
      if (!sameIds(ids, this.ids)) {
        this.ids = ids;
        // The saved analysis's controls stay checked.
        this.selectionKey = this.periodKey;
      }
    } else {
      this.ids = ccPruneIds(this.units, this.ids);
    }
    this.periodsChanged();
  }

  /** The run choices or the units may have changed: the controls follow, and the outer wizard hears of it. */
  private periodsChanged(): void {
    this.syncControls();
    this.changed();
  }

  // --- Presets ---

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

  /**
   * The most recent saved analysis of this subject, within the same battery or suite while a set is
   * compared: the last look *Confirm on later data* starts after.
   */
  get lastAnalysis(): CcAnalysisSummary | null {
    const key = this.axis?.key;
    if (!key) return null;
    const setKey = this.compareSet?.key ?? null;
    return [...this.analyses]
      .filter(analysis => analysis.subjectModelKey === key && (setKey === null || analysis.comparisonSetKey === setKey))
      .sort((a, b) => utcMillis(b.createdAtUtc) - utcMillis(a.createdAtUtc))[0] ?? null;
  }

  /** Applies a preset to the step-1 units; *Custom* keeps the current choices. */
  choosePreset(preset: CcPreset): void {
    this.preset = preset;
    this.presetNote = '';
    this.presetAnchorUtc = null;
    this.resultUnits = null;
    const units = this.units;
    const battery = this.batteryMode;
    if (this.axis) {
      switch (preset) {
        case 'earliest':
          this.applyPreset(ccEarliestVsLatest(units, battery));
          break;
        case 'annotation': {
          const annotation = this.annotations.find(a => a.id === this.presetAnnotationId) ?? this.annotations[0];
          if (!annotation) {
            this.applyPreset({ ids: CC_NO_PERIOD_IDS, note: 'This model has no annotation in the timeline range.', anchorUtc: null });
            break;
          }
          this.presetAnnotationId = annotation.id;
          this.applyPreset(ccBeforeAfter(units, annotation.atUtc, battery, 'the annotation'));
          break;
        }
        case 'event': {
          const groups = this.eventGroups;
          const group = groups.find(g => g.key === this.presetEventGroupKey) ?? groups[0];
          if (!group) {
            this.applyPreset({ ids: CC_NO_PERIOD_IDS, note: 'No Overseer change was detected in the timeline range.', anchorUtc: null });
            break;
          }
          this.presetEventGroupKey = group.key;
          this.applyPreset(ccBeforeAfter(units, group.atUtc, battery, `the Overseer change ${group.tag}`));
          break;
        }
        case 'later': {
          const last = this.lastAnalysis;
          if (!last) {
            const note = this.compareSet
              ? `This model has no saved analysis of ${this.compareSet.label} yet; there is no earlier look to confirm.`
              : 'This model has no saved analysis yet; there is no earlier look to confirm.';
            this.applyPreset({ ids: CC_NO_PERIOD_IDS, note, anchorUtc: null });
            break;
          }
          this.applyPreset(ccConfirmOnLaterData(units, last, battery));
          break;
        }
        case 'custom':
          this.ids = ccPruneIds(units, this.ids);
          break;
      }
    }
    this.periodsChanged();
  }

  private applyPreset(outcome: CcPresetOutcome): void {
    this.ids = sameIds(outcome.ids, this.ids) ? this.ids : outcome.ids;
    this.presetNote = outcome.note;
    this.presetAnchorUtc = outcome.anchorUtc;
  }

  onAnnotationPick(event: Event): void {
    this.presetAnnotationId = Number((event.target as HTMLSelectElement).value);
    this.choosePreset('annotation');
  }

  onEventPick(event: Event): void {
    this.presetEventGroupKey = (event.target as HTMLSelectElement).value;
    this.choosePreset('event');
  }

  /** A run choice made by hand: the preset becomes *Custom*. */
  onRunPick(choice: CcRunChoice, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.ids = { ...this.ids, [choice]: value === '' ? null : Number(value) };
    this.preset = 'custom';
    this.presetNote = '';
    this.presetAnchorUtc = null;
    this.resultUnits = null;
    this.periodsChanged();
  }

  /**
   * Which units the presets span: `Presets use the runs chosen in step 1: #21 (2026-09-20) to #93
   * (2026-10-05), 15 runs.`, or every run in the dates while step 1 leaves the selection alone; in a
   * battery set `Presets use the battery runs chosen in step 1: #11 (2026-10-08) to #12 (2026-10-08),
   * 2 battery runs.`
   */
  get spanNote(): string {
    const battery = this.batteryMode;
    const units = battery
      ? this.batteryRows.map(row => ({ id: row.batteryRunId, startedAtUtc: row.startedAtUtc }))
      : this.rows.map(row => ({ id: row.runId, startedAtUtc: row.startedAtUtc }));
    if (units.length === 0) return '';
    const noun = battery ? 'battery run' : 'run';
    const ordered = units.sort((a, b) => utcMillis(a.startedAtUtc) - utcMillis(b.startedAtUtc) || a.id - b.id);
    const first = ordered[0];
    const last = ordered[ordered.length - 1];
    const runs = first === last
      ? `#${first.id} (${formatUtcDate(first.startedAtUtc)}), 1 ${noun}`
      : `#${first.id} (${formatUtcDate(first.startedAtUtc)}) to #${last.id} (${formatUtcDate(last.startedAtUtc)}), ${plural(ordered.length, noun)}`;
    const incomplete = battery && this.allBatteryRows.some(row => !row.complete);
    return scopeIsDefault(this.scope) && !incomplete
      ? `Presets use every ${noun} in the dates: ${runs}.`
      : `Presets use the ${noun}s chosen in step 1: ${runs}.`;
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

  // --- Controls and preview ---

  private get periodKey(): string {
    const ids = this.ids;
    return `${this.axis?.key}|${ids.baselineFirstId}|${ids.baselineLastId}|${ids.comparisonFirstId}|${ids.comparisonLastId}|${this.scopeKey}`;
  }

  /** Checks every control candidate when the run choices or the step-1 selection changed; true when it did. */
  private syncControls(): boolean {
    const key = this.periodKey;
    if (key === this.selectionKey) return false;
    this.selectionKey = key;
    this.replaceSelection(this.controlSelected, this.controlCandidates);
    return true;
  }

  /**
   * Checks every matched control the first time these run choices reach *Analyze*; the outer wizard
   * calls it on entering that step. A change of the choices does the same by itself.
   */
  preselectRuns(): void {
    if (this.syncControls()) this.changed();
  }

  /** The units left out in step 1 that started inside either window, ascending: battery run ids in a battery set. */
  get leftOutInPeriods(): number[] {
    const windows = this.windows;
    if (this.scope.leftOut.size === 0 || !windows) return [];
    const units = this.batteryMode
      ? this.allBatteryRows.map(row => ({ id: row.batteryRunId, startedAtUtc: row.startedAtUtc }))
      : this.allRows.map(row => ({ id: row.runId, startedAtUtc: row.startedAtUtc }));
    const inside = (at: number, start: string, end: string) => at >= utcMillis(start) && at <= utcMillis(end);
    return units
      .filter(unit => {
        if (!this.scope.leftOut.has(unit.id)) return false;
        const at = utcMillis(unit.startedAtUtc);
        return inside(at, windows.baselineStartUtc, windows.baselineEndUtc)
          || inside(at, windows.comparisonStartUtc, windows.comparisonEndUtc);
      })
      .map(unit => unit.id)
      .sort((a, b) => a - b);
  }

  /** `#45, #51`. */
  runList(ids: readonly number[]): string {
    return ids.map(id => `#${id}`).join(', ');
  }

  /** The runs of a period's eligible units: the members of its battery runs in a battery set. */
  periodRows(period: CcPeriod): CcRunRow[] {
    return this.periodUnits(period).flatMap(unit => unit.runs);
  }

  private replaceSelection(set: Set<number>, ids: readonly number[]): void {
    set.clear();
    for (const id of ids) set.add(id);
  }

  toggleControl(runId: number, event: Event): void {
    if ((event.target as HTMLInputElement).checked) this.controlSelected.add(runId); else this.controlSelected.delete(runId);
    this.changed();
  }

  onRelaxedPooling(event: Event): void {
    this.relaxedPooling = (event.target as HTMLInputElement).checked;
    this.changed();
  }

  /** The control runs matched to any run of the eligible units in either period, ascending. */
  get controlCandidates(): number[] {
    const ids = new Set<number>();
    for (const row of [...this.periodRows('baseline'), ...this.periodRows('comparison')]) {
      for (const id of row.matchedControlRunIds) ids.add(id);
    }
    return [...ids].sort((a, b) => a - b);
  }

  /** The checked control candidates, ascending. */
  get selectedControls(): number[] {
    return this.controlCandidates.filter(id => this.controlSelected.has(id));
  }

  /** The runs a re-grade covers: the runs of both periods (the members of their battery runs) and the checked controls. */
  get regradeRunIds(): number[] {
    const targets = [...this.periodRows('baseline'), ...this.periodRows('comparison')].map(row => row.runId);
    return [...new Set([...targets, ...this.selectedControls])].sort((a, b) => a - b);
  }

  private pointsOf(ids: ReadonlySet<number>): CcTimelinePoint[] {
    return (this.timeline?.points ?? []).filter(point => ids.has(point.runId));
  }

  /** The time strata both periods' runs sampled. */
  get commonStrata(): string[] {
    const strataOf = (period: CcPeriod) =>
      new Set(this.pointsOf(new Set(this.periodRows(period).map(row => row.runId))).flatMap(point => point.strata));
    const baseline = strataOf('baseline');
    const comparison = strataOf('comparison');
    return [...baseline].filter(stratum => comparison.has(stratum)).sort();
  }

  /** The composite Overseer events between the baseline's start and the comparison's end. */
  get eventGroupsInSpan(): CcEventGroup[] {
    const windows = this.windows;
    if (!windows) return [];
    const start = utcMillis(windows.baselineStartUtc);
    const end = utcMillis(windows.comparisonEndUtc);
    return this.eventGroups.filter(group => {
      const at = utcMillis(group.atUtc);
      return at >= start && at <= end;
    });
  }

  /** The runs of both periods without a matched control run; the members of their battery runs in a battery set. */
  get missingControls(): CcRunRow[] {
    return [...this.periodRows('baseline'), ...this.periodRows('comparison')]
      .filter(row => row.matchedControlRunIds.length === 0);
  }

  get analyzeBlocked(): string {
    if (!this.axis) return 'Choose a model in step 1 first.';
    if (this.periodsError) return this.periodsError;
    if (this.overridesError) return this.overridesError;
    return '';
  }

  started(unit: CcPeriodUnit): string {
    return formatUtcDateTime(unit.startedAtUtc);
  }

  /** A unit's suite, or `Board Suite, Wiki Suite` for a battery run's members, in suite order. */
  unitSuites(unit: CcPeriodUnit): string {
    return [...new Set(unit.runs.map(run => run.suiteName))].join(', ') || '—';
  }

  /** The matched control runs of a unit: of a battery run's members in a battery set. */
  unitControls(unit: CcPeriodUnit): string {
    if (!unit.battery) return controlRunsText(unit.runs[0]);
    const ids = [...new Set(unit.runs.flatMap(member => member.matchedControlRunIds))].sort((a, b) => a - b);
    return ids.length === 0 ? 'None' : ids.map(id => `#${id}`).join(', ');
  }

  /** The request Analyze sends. */
  buildRequest(): CcAnalysisRequest | null {
    const axis = this.axis;
    const windows = this.windows;
    if (!axis || !windows || this.analyzeBlocked) return null;
    const idsOf = (period: CcPeriod) => this.periodUnits(period).map(unit => unit.id).sort((a, b) => a - b);
    const baseline = idsOf('baseline');
    const comparison = idsOf('comparison');
    const request: CcAnalysisRequest = {
      subjectModelKey: axis.key,
      ...windows,
      relaxedPooling: this.relaxedPooling,
      runSelection: this.runSelection()
    };
    // A battery set takes the battery runs and refuses run ids; a suite set and no set take run ids.
    if (this.batteryMode) {
      request.baselineBatteryRunIds = baseline;
      request.comparisonBatteryRunIds = comparison;
    } else {
      request.baselineRunIds = baseline;
      request.comparisonRunIds = comparison;
    }
    if (this.compareSet) request.comparisonSet = { kind: this.compareSet.kind, key: this.compareSet.key };
    const name = this.name.trim();
    if (name) request.name = name;
    // Without matched candidates the server chooses the controls itself.
    if (this.controlCandidates.length > 0) request.controlRunIds = this.selectedControls;
    const overrides = this.protocolOverrides();
    if (overrides) request.protocolOverrides = overrides;
    return request;
  }

  /**
   * The step-1 selection as the analysis records it; sent with every request, the default one
   * included. In a battery set the marks and left-out ids are battery runs' and go in the battery fields.
   */
  private runSelection(): CcRunSelection {
    const bounds = ccRangeBounds(this.range);
    const leftOut = [...this.scope.leftOut].sort((a, b) => a - b);
    const selection: CcRunSelection = {
      rangeLabel: ccDateRangeText(this.range),
      rangeFromUtc: bounds.fromUtc,
      rangeToUtc: bounds.toUtc,
      firstRunId: this.batteryMode ? null : this.scope.firstRunId,
      lastRunId: this.batteryMode ? null : this.scope.lastRunId,
      leftOutRunIds: this.batteryMode ? [] : leftOut
    };
    if (this.batteryMode) {
      selection.firstBatteryRunId = this.scope.firstRunId;
      selection.lastBatteryRunId = this.scope.lastRunId;
      selection.leftOutBatteryRunIds = leftOut;
    }
    return selection;
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
    this.controlSelected.clear();
    this.selectionKey = null;
    this.resultUnits = null;
    this.ids = CC_NO_PERIOD_IDS;
    this.choosePreset(this.axis ? 'earliest' : 'custom');
  }
}
