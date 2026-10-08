import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Injector,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  afterNextRender,
  inject
} from '@angular/core';

import { CardListChip, CardListFacet, CardListSort, CardListState } from '../../../../shared/data-table/card-list-state';
import { FilterFacetComponent } from '../../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../../shared/data-table/table-state';
import { DateFieldComponent } from '../../../../shared/date-field/date-field.component';
import { ModelPickerComponent, ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import {
  axisText,
  batteryRunStatusText,
  controlRunsText,
  formatUtcDate,
  formatUtcDateTime,
  isUtcDateInput,
  plural,
  regradeCoverageText,
  runStatusText,
  segmentText,
  servedModelsText,
  utcMillis
} from '../chat-consistency-format';
import {
  CC_ALL_DATES,
  CC_RANGE_PRESETS,
  CcDateRange,
  CcRangePreset,
  ccAnchorRange,
  ccPresetToCustom,
  ccRangeBounds
} from '../chat-consistency-range';
import {
  CC_EMPTY_SCOPE,
  CC_INCLUSION_TEXT,
  CcRunInclusion,
  CcRunScope,
  CcScopeUnit,
  batterySetRuns,
  ccEmptyScope,
  isBatteryRunRow,
  notAnalyzedRuns,
  scopeChangeCount,
  scopeIsDefault,
  scopeRuns,
  setFirstRun,
  setLastRun,
  suiteSetRuns,
  toggleLeftOut,
  unitIdOf
} from '../chat-consistency-scope';
import {
  CC_AXES,
  CcAxis,
  CcAxisEligibility,
  CcBatteryRunRow,
  CcComparisonSet,
  CcComparisonSets,
  CcModelAxis,
  CcRunRow,
  CcTimeline
} from '../chat-consistency.models';

/** A model axis as the picker renders it. */
export interface CcAxisPickerModel {
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
}

/** The picker's options: the model axes that have runs, as the server orders them. */
export function axisPickerOptions(axes: readonly CcModelAxis[]): ModelPickerOption<CcAxisPickerModel>[] {
  return axes
    .filter(axis => axis.runCount > 0)
    .map(axis => ({
      key: axis.key,
      model: { displayName: axis.displayName, provider: axis.provider, modelId: axis.modelId, thinkingLevel: axis.thinkingLevel }
    }));
}

/** A comparison set as the Compare select offers it: `Two initial suites (revision 1) · 2 battery runs`. */
export function compareOptionLabel(set: CcComparisonSet): string {
  return `${set.label} · ${plural(set.unitCount, set.kind === 'battery' ? 'battery run' : 'run')}`;
}

/** Where the run list's Sort by choice is kept, per browser. */
export const CC_RUNS_VIEW_STORAGE_KEY = 'overseer.benchmark.chatConsistency.runs.view';

/** Where the battery-run list's Sort by choice is kept, per browser. */
export const CC_BATTERY_RUNS_VIEW_STORAGE_KEY = 'overseer.benchmark.chatConsistency.batteryRuns.view';

/** The orders Sort by offers for the run cards. */
export const CC_RUN_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'started', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'started', direction: 'asc' },
  { id: 'suite', label: 'Suite (A–Z)', column: 'suite', direction: 'asc' },
  { id: 'harness', label: 'Harness', column: 'harness', direction: 'desc' }
];

/** The orders Sort by offers for the battery-run cards. */
export const CC_BATTERY_RUN_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'started', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'started', direction: 'asc' },
  { id: 'harness', label: 'Harness', column: 'harness', direction: 'desc' }
];

/** The *In the analysis* facet's values, in order. */
const INCLUSION_ORDER: readonly CcRunInclusion[] = ['included', 'leftOut', 'beforeSpan', 'afterSpan', 'incomplete'];

const INCLUSION_LABELS: Readonly<Record<CcRunInclusion, string>> = {
  included: 'Included',
  leftOut: 'Left out',
  beforeSpan: 'Before the first run',
  afterSpan: 'After the last run',
  incomplete: 'Incomplete',
  outsideSet: 'Outside the compared set'
};

/** The *Origin* facet's values, in order: a run of its own, or a member of a battery run. */
const ORIGIN_STANDALONE = 'Standalone';
const ORIGIN_MEMBER = 'Battery member';

/** How many left-out units the selection band names before *+N more*. */
const BAND_LEFT_OUT_CHIPS = 6;

/** One chip of the selection band: a first or last unit mark, or a left-out unit. */
export interface CcScopeChip {
  key: string;
  kind: 'first' | 'last' | 'leftOut';
  runId: number;
  label: string;
  removeLabel: string;
  tip: string;
}

/** A harness version's sort value: its leading number, else -1. */
function harnessNumber(version: string | null | undefined): number {
  const value = Number.parseFloat(version ?? '');
  return Number.isFinite(value) ? value : -1;
}

function harnessValue(row: CcRunRow): number {
  return harnessNumber(row.harnessVersion);
}

/** A battery run's highest member harness, for Sort by *Harness*. */
function batteryHarnessValue(row: CcBatteryRunRow): number {
  return Math.max(-1, ...row.harnessVersions.map(harnessNumber));
}

/** Whether the run is a member of a battery run. */
function originOf(row: CcRunRow): string {
  return row.batteryRunId !== null && row.batteryRunId !== undefined ? ORIGIN_MEMBER : ORIGIN_STANDALONE;
}

/** The text the search field matches: `#id`, suite, harness, status and served model ids. */
function runSearchText(row: CcRunRow): string {
  return [
    `#${row.runId}`,
    row.suiteName,
    row.harnessVersion ?? '',
    runStatusText(row.status),
    ...row.servedModelIds.map(served => served.modelId)
  ].join('\n').toLowerCase();
}

/** The text the battery-run search matches: `#id`, the battery, harnesses, status and the members' ids and suites. */
function batteryRunSearchText(row: CcBatteryRunRow): string {
  return [
    `#${row.batteryRunId}`,
    row.batteryName,
    ...row.harnessVersions,
    batteryRunStatusText(row.status),
    ...row.members.flatMap(member => [`#${member.runId}`, member.suiteName])
  ].join('\n').toLowerCase();
}

/**
 * Step 1 of the Chat Consistency wizard: the subject and the UTC dates in one row, the battery or
 * suite to compare within, then the units as a card list with the shared filter bar — battery runs in
 * a battery set, runs otherwise — and the selection band that chooses which of them the analysis
 * uses: a first and a last unit, and units left out. The filters change what is shown, never the
 * selection. Presentational: the host loads the data, keeps the selection and performs the actions
 * this step asks for.
 */
@Component({
  selector: 'app-cc-model-step',
  standalone: true,
  imports: [ModelPickerComponent, DateFieldComponent, FilterFacetComponent],
  templateUrl: './model-step.component.html',
  styleUrls: ['./model-step.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcModelStepComponent implements OnInit, OnChanges, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly injector = inject(Injector);

  @Input() axes: readonly CcModelAxis[] = [];
  @Input() axesLoading = false;
  @Input() axesError: string | null = null;
  @Input() selectedKey: string | null = null;
  @Input() range: CcDateRange = CC_ALL_DATES;
  /** The units the analysis uses: the first and last unit marks and the units left out. */
  @Input() scope: CcRunScope = CC_EMPTY_SCOPE;
  /** The batteries and suites the subject can be compared within; null until loaded. */
  @Input() comparisonSets: CcComparisonSets | null = null;
  /** The compared set; null lists every run, as no set narrows them. */
  @Input() compareKey: string | null = null;
  @Input() timeline: CcTimeline | null = null;
  @Input() rows: readonly CcRunRow[] = [];
  /** Every battery run of the subject in the dates. */
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  @Input() loading = false;
  @Input() timelineError: string | null = null;
  @Input() runsError: string | null = null;
  /** Runs whose anchor mark is being saved. */
  @Input() anchorBusy: ReadonlySet<number> = new Set<number>();
  @Input() anchorError: string | null = null;
  /** A one-off confirmation for the list's status line. */
  @Input() announcement = '';
  /** Why the model, the dates and the compared set cannot change now; empty while they can. */
  @Input() lockedReason = '';

  @Output() readonly modelChange = new EventEmitter<string>();
  @Output() readonly rangeChange = new EventEmitter<CcDateRange>();
  @Output() readonly compareChange = new EventEmitter<string>();
  @Output() readonly scopeChange = new EventEmitter<CcRunScope>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly anchorToggle = new EventEmitter<CcRunRow>();
  @Output() readonly openRunReport = new EventEmitter<number>();
  @Output() readonly openBatteryRunReport = new EventEmitter<number>();

  readonly axisOrder = CC_AXES;
  readonly rangePresets = CC_RANGE_PRESETS;
  readonly sorts = CC_RUN_SORTS;
  readonly optionLabel = compareOptionLabel;

  options: ModelPickerOption<CcAxisPickerModel>[] = [];
  preset: CcRangePreset = 'all';
  fromDay = '';
  toDay = '';
  rangeError: string | null = null;
  /** What the last change to the selection did besides itself, such as clearing a mark. */
  scopeNote = '';
  /** The run whose More actions popover is open. */
  moreOpenId: number | null = null;

  readonly table = new TableState<CcRunRow>('started', 'desc').registerAccessors(
    {
      started: row => utcMillis(row.startedAtUtc),
      suite: row => row.suiteName.toLowerCase(),
      harness: row => harnessValue(row)
    },
    {
      search: customFilter((row, value) => runSearchText(row).includes(value.trim().toLowerCase())),
      suite: anyOfFilter(row => row.suiteName),
      origin: anyOfFilter(row => originOf(row)),
      harness: anyOfFilter(row => row.harnessVersion ?? ''),
      telemetry: anyOfFilter(row => (row.isLegacy ? 'Legacy' : 'Recorded')),
      inclusion: anyOfFilter(row => this.inclusionOf(row)),
      eligibility: anyOfFilter(row => row.eligibility.filter(entry => entry.eligible).map(entry => entry.axis))
    }
  );

  /** The run cards over `table`: the search, Sort by, the facets, the chips and the batch. */
  readonly list = new CardListState<CcRunRow>(this.table, {
    idPrefix: 'cc-runs',
    batch: 10,
    sorts: CC_RUN_SORTS,
    defaultSort: 'newest',
    storageKey: CC_RUNS_VIEW_STORAGE_KEY,
    facets: [
      // A suite set lists one suite, so its runs are told apart by where they come from instead.
      { column: 'suite', label: 'Suite', values: row => row.suiteName, enabled: () => !this.suiteMode },
      {
        column: 'origin', label: 'Origin', values: row => originOf(row), order: [ORIGIN_STANDALONE, ORIGIN_MEMBER],
        enabled: () => this.suiteMode
      },
      { column: 'harness', label: 'Harness', values: row => row.harnessVersion ?? '', order: (a, b) => Number(b) - Number(a) || a.localeCompare(b) },
      { column: 'telemetry', label: 'Telemetry', values: row => (row.isLegacy ? 'Legacy' : 'Recorded'), order: ['Recorded', 'Legacy'] },
      {
        column: 'inclusion', label: 'In the analysis', values: row => this.inclusionOf(row), order: INCLUSION_ORDER,
        labelOf: value => INCLUSION_LABELS[value as CcRunInclusion] ?? value
      },
      {
        column: 'eligibility', label: 'Eligibility',
        values: row => row.eligibility.filter(entry => entry.eligible).map(entry => entry.axis),
        order: CC_AXES, labelOf: value => axisText(value as CcAxis)
      }
    ],
    memoDeps: () => [this.scope, this.compareKey],
    onChange: () => this.cdr.markForCheck()
  });

  readonly batteryTable = new TableState<CcBatteryRunRow>('started', 'desc').registerAccessors(
    {
      started: row => utcMillis(row.startedAtUtc),
      harness: row => batteryHarnessValue(row)
    },
    {
      search: customFilter((row, value) => batteryRunSearchText(row).includes(value.trim().toLowerCase())),
      harness: anyOfFilter(row => row.harnessVersions),
      inclusion: anyOfFilter(row => this.inclusionOf(row)),
      eligibility: anyOfFilter(row => row.eligibility.filter(entry => entry.eligible).map(entry => entry.axis))
    }
  );

  /** The battery-run cards of a battery set over `batteryTable`. */
  readonly batteryList = new CardListState<CcBatteryRunRow>(this.batteryTable, {
    idPrefix: 'cc-bruns',
    batch: 10,
    sorts: CC_BATTERY_RUN_SORTS,
    defaultSort: 'newest',
    storageKey: CC_BATTERY_RUNS_VIEW_STORAGE_KEY,
    facets: [
      { column: 'harness', label: 'Harness', values: row => row.harnessVersions, order: (a, b) => Number(b) - Number(a) || a.localeCompare(b) },
      {
        column: 'inclusion', label: 'In the analysis', values: row => this.inclusionOf(row), order: INCLUSION_ORDER,
        labelOf: value => INCLUSION_LABELS[value as CcRunInclusion] ?? value
      },
      {
        column: 'eligibility', label: 'Eligibility',
        values: row => row.eligibility.filter(entry => entry.eligible).map(entry => entry.axis),
        order: CC_AXES, labelOf: value => axisText(value as CcAxis)
      }
    ],
    memoDeps: () => [this.scope, this.compareKey],
    onChange: () => this.cdr.markForCheck()
  });

  /** The selection derived from the units and the scope, kept until either object changes. */
  private scopeMemo: {
    units: readonly CcScopeUnit[];
    scope: CcRunScope;
    scoped: CcScopeUnit[];
    inclusion: ReadonlyMap<number, CcRunInclusion>;
    chips: CcScopeChip[];
    moreLeftOut: number;
  } | null = null;

  /** The runs and the battery runs of the compared set, kept until the rows or the set change. */
  private setMemo: {
    rows: readonly CcRunRow[];
    batteryRows: readonly CcBatteryRunRow[];
    key: string | null;
    listRows: readonly CcRunRow[];
    batteryListRows: readonly CcBatteryRunRow[];
  } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['axes']) {
      this.options = axisPickerOptions(this.axes);
    }
    if (changes['range']) {
      this.preset = this.range.preset;
      this.fromDay = this.range.fromDay;
      this.toDay = this.range.toDay;
      this.rangeError = null;
    }
    const keyChange = changes['selectedKey'];
    const setChange = changes['compareKey'];
    if ((keyChange && !keyChange.firstChange && keyChange.previousValue !== keyChange.currentValue)
      || (setChange && !setChange.firstChange && setChange.previousValue !== setChange.currentValue)) {
      this.list.reset();
      this.batteryList.reset();
      this.scopeNote = '';
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.batteryList.dispose();
  }

  get selectedAxis(): CcModelAxis | null {
    return this.axes.find(axis => axis.key === this.selectedKey) ?? null;
  }

  /** The chosen model's name for the runs heading: the axis list's, else the loaded timeline's. */
  get selectedName(): string {
    return this.selectedAxis?.displayName ?? this.timeline?.subject.displayName ?? 'the model';
  }

  get pickerEmptyHint(): string {
    return this.axesError ?? (this.axesLoading ? 'Loading the models…' : 'No model has benchmark runs yet.');
  }

  /** The picker's description: its hint, and the lock reason while locked. */
  get modelDescribedBy(): string {
    return this.lockedReason ? 'cc-tl-model-hint cc-tl-lock-reason' : 'cc-tl-model-hint';
  }

  /** The Dates select's description: the rolling window's hint and the lock reason, whichever are shown. */
  get rangeDescribedBy(): string | null {
    const ids = [this.rollingHint ? 'cc-tl-range-hint' : '', this.lockedReason ? 'cc-tl-lock-reason' : ''].filter(Boolean);
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /** A date field's further description: the range error and the lock reason, whichever are shown. */
  get dateDescribedBy(): string {
    return [this.rangeError ? 'cc-tl-range-error' : '', this.lockedReason ? 'cc-tl-lock-reason' : ''].filter(Boolean).join(' ');
  }

  /** `Since 2026-09-30 14:05 UTC · Reload runs moves it to now`, while a rolling preset is chosen. */
  get rollingHint(): string {
    if (this.range.preset === 'all' || this.range.preset === 'custom') return '';
    const { fromUtc } = ccRangeBounds(this.range);
    return fromUtc ? `Since ${formatUtcDateTime(fromUtc)} · Reload runs moves it to now` : '';
  }

  selectModel(key: string | number | null): void {
    if (this.lockedReason) return;
    if (typeof key === 'string' && key !== this.selectedKey) {
      this.modelChange.emit(key);
    }
  }

  // --- Dates ---

  /**
   * The Dates select changed: a rolling preset counts back from now, *Custom* starts from the dates
   * the current choice covers. While locked the stored preset is written back to the select.
   */
  onPresetChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    if (this.lockedReason) {
      select.value = this.range.preset;
      return;
    }
    const preset = select.value as CcRangePreset;
    if (!this.rangePresets.some(option => option.id === preset) || preset === this.range.preset) return;
    this.preset = preset;
    this.rangeError = null;
    if (preset === 'all') {
      this.rangeChange.emit(CC_ALL_DATES);
    } else if (preset === 'custom') {
      const custom = ccPresetToCustom(this.range);
      this.fromDay = custom.fromDay;
      this.toDay = custom.toDay;
      this.rangeChange.emit(custom);
    } else {
      this.rangeChange.emit(ccAnchorRange({ preset, fromDay: '', toDay: '', anchorUtc: null }, new Date()));
    }
    this.cdr.markForCheck();
  }

  /** A custom date changed: a valid range is applied at once, an invalid one is explained. */
  onDayChange(which: 'from' | 'to', value: string): void {
    if (this.lockedReason) {
      this.cdr.markForCheck();
      return;
    }
    if (which === 'from') this.fromDay = value; else this.toDay = value;
    if ((this.fromDay && !isUtcDateInput(this.fromDay)) || (this.toDay && !isUtcDateInput(this.toDay))) {
      this.rangeError = 'Enter the dates as complete calendar dates, YYYY-MM-DD.';
    } else if (this.fromDay && this.toDay && this.fromDay > this.toDay) {
      this.rangeError = 'The start date must not be after the end date.';
    } else {
      this.rangeError = null;
      this.rangeChange.emit({ preset: 'custom', fromDay: this.fromDay, toDay: this.toDay, anchorUtc: null });
    }
    this.cdr.markForCheck();
  }

  // --- The compared set ---

  get sets(): readonly CcComparisonSet[] {
    return this.comparisonSets?.sets ?? [];
  }

  get batterySets(): CcComparisonSet[] {
    return this.sets.filter(set => set.kind === 'battery');
  }

  get suiteSets(): CcComparisonSet[] {
    return this.sets.filter(set => set.kind === 'suite');
  }

  /** The compared set, when the sets offer it. */
  get compareSet(): CcComparisonSet | null {
    return this.sets.find(set => set.key === this.compareKey) ?? null;
  }

  /** A battery set is compared: the cards are battery runs. */
  get batteryMode(): boolean {
    return this.compareSet?.kind === 'battery';
  }

  /** A suite set is compared: the cards are the suite's runs, battery members among them. */
  get suiteMode(): boolean {
    return this.compareSet?.kind === 'suite';
  }

  /** `run` or `battery run`: what the list, the band and the notes count. */
  get noun(): string {
    return this.batteryMode ? 'battery run' : 'run';
  }

  /** The Compare select's description: its hint, and the lock reason while locked. */
  get compareDescribedBy(): string {
    return this.lockedReason ? 'cc-compare-hint cc-tl-lock-reason' : 'cc-compare-hint';
  }

  /** The Compare select changed. While locked, or with no set offered, the current choice is written back. */
  onCompareChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    if (this.lockedReason || this.sets.length === 0) {
      select.value = this.compareKey ?? '';
      return;
    }
    const key = select.value;
    if (key === this.compareKey || !this.sets.some(set => set.key === key)) return;
    this.compareChange.emit(key);
  }

  private setState(): NonNullable<CcModelStepComponent['setMemo']> {
    const memo = this.setMemo;
    const key = this.compareSet?.key ?? null;
    if (memo && memo.rows === this.rows && memo.batteryRows === this.batteryRows && memo.key === key) return memo;
    this.setMemo = {
      rows: this.rows,
      batteryRows: this.batteryRows,
      key,
      listRows: suiteSetRuns(this.rows, key),
      batteryListRows: batterySetRuns(this.batteryRows, key)
    };
    return this.setMemo;
  }

  /** The runs the run cards list: the suite's in a suite set, every run without a set. */
  get listRows(): readonly CcRunRow[] {
    return this.setState().listRows;
  }

  /** The battery runs of the battery set. */
  get batteryListRows(): readonly CcBatteryRunRow[] {
    return this.setState().batteryListRows;
  }

  /** The units the selection counts. */
  get unitRows(): readonly CcScopeUnit[] {
    return this.batteryMode ? this.batteryListRows : this.listRows;
  }

  // --- The selection ---

  private scopeState(): NonNullable<CcModelStepComponent['scopeMemo']> {
    const memo = this.scopeMemo;
    const units = this.unitRows;
    if (memo && memo.units === units && memo.scope === this.scope) return memo;
    const scope = this.scope;
    const noun = this.noun;
    const chips: CcScopeChip[] = [];
    if (scope.firstRunId !== null) {
      chips.push({
        key: 'first', kind: 'first', runId: scope.firstRunId, label: `First: #${scope.firstRunId}`,
        removeLabel: `Remove the first-run mark from ${noun} #${scope.firstRunId}`, tip: 'Remove the first-run mark'
      });
    }
    if (scope.lastRunId !== null) {
      chips.push({
        key: 'last', kind: 'last', runId: scope.lastRunId, label: `Last: #${scope.lastRunId}`,
        removeLabel: `Remove the last-run mark from ${noun} #${scope.lastRunId}`, tip: 'Remove the last-run mark'
      });
    }
    const leftOut = [...scope.leftOut].sort((a, b) => a - b);
    for (const runId of leftOut.slice(0, BAND_LEFT_OUT_CHIPS)) {
      chips.push({
        key: `left-${runId}`, kind: 'leftOut', runId, label: `Left out: #${runId}`,
        removeLabel: `Include ${noun} #${runId} again`, tip: 'Include again'
      });
    }
    this.scopeMemo = {
      units,
      scope,
      scoped: scopeRuns(units, scope),
      inclusion: notAnalyzedRuns(units, scope),
      chips,
      moreLeftOut: Math.max(0, leftOut.length - BAND_LEFT_OUT_CHIPS)
    };
    return this.scopeMemo;
  }

  inclusionOf(row: CcScopeUnit): CcRunInclusion {
    return this.scopeState().inclusion.get(unitIdOf(row)) ?? 'included';
  }

  get scopedRows(): readonly CcScopeUnit[] {
    return this.scopeState().scoped;
  }

  get scopeChips(): readonly CcScopeChip[] {
    return this.scopeState().chips;
  }

  get moreLeftOut(): number {
    return this.scopeState().moreLeftOut;
  }

  get scopeIsDefault(): boolean {
    return scopeIsDefault(this.scope);
  }

  get scopeChangeCount(): number {
    return scopeChangeCount(this.scope);
  }

  /** The battery runs of the set that are never analyzed, for want of a usable member in a suite slot. */
  get incompleteCount(): number {
    return this.batteryMode ? this.batteryListRows.filter(row => !row.complete).length : 0;
  }

  /**
   * `Runs in the analysis — 15 of 19 runs in these dates · from #21 (2026-09-20) to #93 (2026-10-05) ·
   * 2 left out`, or `… — all 19 runs in these dates` while nothing is chosen; `Battery runs in the
   * analysis — …` in a battery set, with `· 1 incomplete` while a battery run is.
   */
  get scopeSummary(): string {
    const total = this.unitRows.length;
    const noun = this.noun;
    const title = this.batteryMode ? 'Battery runs in the analysis' : 'Runs in the analysis';
    if (this.scopeIsDefault && this.incompleteCount === 0) {
      return total === 1 ? `${title} — the one ${noun} in these dates` : `${title} — all ${total} ${noun}s in these dates`;
    }
    const scoped = this.scopedRows;
    let text = `${title} — ${scoped.length} of ${plural(total, noun)} in these dates`;
    if (scoped.length > 0) {
      const first = scoped[0];
      const last = scoped[scoped.length - 1];
      text += first === last
        ? ` · only #${unitIdOf(first)} (${formatUtcDate(first.startedAtUtc)})`
        : ` · from #${unitIdOf(first)} (${formatUtcDate(first.startedAtUtc)}) to #${unitIdOf(last)} (${formatUtcDate(last.startedAtUtc)})`;
    }
    if (this.scope.leftOut.size > 0) text += ` · ${this.scope.leftOut.size} left out`;
    if (this.incompleteCount > 0) text += ` · ${this.incompleteCount} incomplete`;
    return text;
  }

  /** The amber warning when every unit is out of the analysis. */
  get emptyScopeText(): string {
    return `No ${this.noun} is left in the analysis. Check at least one ${this.noun}.`;
  }

  isFirst(row: CcScopeUnit): boolean {
    return this.scope.firstRunId === unitIdOf(row);
  }

  isLast(row: CcScopeUnit): boolean {
    return this.scope.lastRunId === unitIdOf(row);
  }

  /**
   * `Before the first run (#21)`, `After the last run (#93)`, `Before the first battery run (#11)`,
   * `Incomplete: 1 of 2 suites usable`; empty for a unit inside the span.
   */
  outReason(row: CcScopeUnit): string {
    switch (this.inclusionOf(row)) {
      case 'beforeSpan': return `Before the first ${this.noun} (#${this.scope.firstRunId})`;
      case 'afterSpan': return `After the last ${this.noun} (#${this.scope.lastRunId})`;
      case 'incomplete': return `Incomplete: ${isBatteryRunRow(row) && row.incompleteReason ? row.incompleteReason : 'a suite has no usable run'}`;
      default: return '';
    }
  }

  /** An incomplete battery run is never analyzed, so it cannot bound the analysis either. */
  markRefused(row: CcScopeUnit): boolean {
    return isBatteryRunRow(row) && !row.complete;
  }

  /** The include checkbox: a unit before or after the span, or incomplete, refuses the click; any other toggles left out. */
  onIncludeClick(row: CcScopeUnit, event: Event): void {
    if (this.outReason(row)) {
      event.preventDefault();
      return;
    }
    const include = (event.target as HTMLInputElement).checked;
    this.applyScope(toggleLeftOut(this.scope, unitIdOf(row), include), '');
  }

  toggleFirst(row: CcScopeUnit): void {
    if (this.markRefused(row)) return;
    const next = setFirstRun(this.scope, this.unitRows, this.isFirst(row) ? null : unitIdOf(row));
    this.applyScope(next.scope, next.note);
  }

  toggleLast(row: CcScopeUnit): void {
    if (this.markRefused(row)) return;
    const next = setLastRun(this.scope, this.unitRows, this.isLast(row) ? null : unitIdOf(row));
    this.applyScope(next.scope, next.note);
  }

  /** Clear selection: every unit in the dates again; focus moves to the band's label. */
  clearScope(): void {
    this.applyScope(ccEmptyScope(this.batteryMode ? 'batteryRun' : 'run'), '');
    this.focusAfterRender(() => this.element('#cc-scope-label'));
  }

  /** Removes one chip, then focuses the chip now in its place, else Clear selection, else the label. */
  removeScopeChip(chip: CcScopeChip, index: number): void {
    let next: CcRunScope;
    switch (chip.kind) {
      case 'first': next = setFirstRun(this.scope, this.unitRows, null).scope; break;
      case 'last': next = setLastRun(this.scope, this.unitRows, null).scope; break;
      default: next = toggleLeftOut(this.scope, chip.runId, true);
    }
    this.applyScope(next, '');
    this.focusAfterRender(() => {
      const buttons = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('.cc-scope-chip-remove'));
      return buttons[index] ?? this.element('.cc-scope-clear') ?? this.element('#cc-scope-label');
    });
  }

  /** *+N more*: the list shows the left-out units. */
  showLeftOut(): void {
    this.activeList.setFacet('inclusion', ['leftOut']);
    this.cdr.markForCheck();
  }

  private applyScope(scope: CcRunScope, note: string): void {
    this.scopeNote = note;
    if (scope !== this.scope) this.scopeChange.emit(scope);
    this.cdr.markForCheck();
  }

  private focusAfterRender(target: () => HTMLElement | null): void {
    afterNextRender(() => target()?.focus(), { injector: this.injector });
  }

  // --- The list ---

  /** The card list the filter bar drives: battery runs in a battery set, runs otherwise. */
  get activeList(): CardListState<CcRunRow> | CardListState<CcBatteryRunRow> {
    return this.batteryMode ? this.batteryList : this.list;
  }

  get activeSorts(): readonly CardListSort[] {
    return this.batteryMode ? CC_BATTERY_RUN_SORTS : CC_RUN_SORTS;
  }

  get cards(): CcRunRow[] {
    return this.list.view(this.listRows);
  }

  get batteryCards(): CcBatteryRunRow[] {
    return this.batteryList.view(this.batteryListRows);
  }

  get facets(): CardListFacet[] {
    return this.batteryMode ? this.batteryList.facets(this.batteryListRows) : this.list.facets(this.listRows);
  }

  get chips(): CardListChip[] {
    return this.batteryMode ? this.batteryList.chips(this.batteryListRows) : this.list.chips(this.listRows);
  }

  /** Empty during the first load, which the status line above the heading announces. */
  get listStatus(): string {
    if (this.loading && this.unitRows.length === 0) return '';
    return this.batteryMode
      ? this.batteryList.statusText(this.batteryListRows, { one: 'battery run', many: 'battery runs' })
      : this.list.statusText(this.listRows, { one: 'run', many: 'runs' });
  }

  get showFilterBar(): boolean {
    return this.unitRows.length > 1 || (this.batteryMode ? this.batteryTable.hasActiveFilters : this.table.hasActiveFilters);
  }

  get noMatches(): boolean {
    return this.batteryMode ? this.batteryTable.noMatches(this.batteryListRows) : this.table.noMatches(this.listRows);
  }

  get remainingCount(): number {
    return this.batteryMode ? this.batteryList.remainingCount(this.batteryListRows) : this.list.remainingCount(this.listRows);
  }

  get nextBatchCount(): number {
    return this.batteryMode ? this.batteryList.nextBatchCount(this.batteryListRows) : this.list.nextBatchCount(this.listRows);
  }

  get matchingCount(): number {
    return this.batteryMode ? this.batteryList.matching(this.batteryListRows).length : this.list.matching(this.listRows).length;
  }

  onSearchInput(event: Event): void {
    this.activeList.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once; in an empty field it passes through. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.activeList.clearSearchOnEscape(event)) {
      this.cdr.detectChanges();
    }
  }

  onSortChange(event: Event): void {
    if (this.activeList.setSort((event.target as HTMLSelectElement).value)) {
      this.cdr.detectChanges();
    }
  }

  onFacetChange(column: string, values: string[]): void {
    this.activeList.setFacet(column, values);
    this.cdr.detectChanges();
  }

  /** Removes a filter chip, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.batteryMode ? this.batteryList.removeChip(chip, this.batteryListRows) : this.list.removeChip(chip, this.listRows);
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.cc-runs-filter-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#cc-runs-search'))?.focus();
  }

  /** Clears the search and every filter, then focuses the search. */
  clearFilters(): void {
    this.activeList.clearFilters();
    this.cdr.detectChanges();
    this.element('#cc-runs-search')?.focus();
  }

  showMore(): void {
    this.focusCard(this.batteryMode ? this.batteryList.showMore(this.batteryListRows) : this.list.showMore(this.listRows));
  }

  showAll(): void {
    this.focusCard(this.batteryMode ? this.batteryList.showAll(this.batteryListRows) : this.list.showAll(this.listRows));
  }

  /** Renders, then focuses the title of the card at `index` in the view, if there is one. */
  private focusCard(index: number): void {
    this.cdr.detectChanges();
    refreshAnchorPositioning();
    if (this.batteryMode) {
      const row = this.batteryCards[index];
      if (row) this.element(`#cc-brun-${row.batteryRunId}-title`)?.focus();
      return;
    }
    const row = this.cards[index];
    if (row) this.element(`#cc-run-${row.runId}-title`)?.focus();
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }

  // --- More actions ---

  onMoreToggle(row: CcRunRow, event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    const popover = this.element(`#cc-run-${row.runId}-more`);
    if (open) {
      this.moreOpenId = row.runId;
      this.cdr.markForCheck();
      refreshAnchorPositioning();
      popover?.querySelector<HTMLElement>('.gh-action-popover-item:not([aria-disabled="true"])')?.focus();
      return;
    }
    if (this.moreOpenId === row.runId) {
      this.moreOpenId = null;
      this.cdr.markForCheck();
    }
    const active = document.activeElement;
    if (!active || active === document.body || !!popover?.contains(active)) {
      this.element(`#cc-run-${row.runId}-more-btn`)?.focus();
    }
  }

  /** Escape closes the popover only; the wizard's dialog stays open. */
  onMoreKeydown(row: CcRunRow, event: KeyboardEvent): void {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    event.stopPropagation();
    this.hideMore(row);
    this.element(`#cc-run-${row.runId}-more-btn`)?.focus();
  }

  onRepeatSetup(row: CcRunRow): void {
    this.hideMore(row);
    this.repeatSetup.emit(row.runId);
  }

  onAnchorToggle(row: CcRunRow): void {
    if (this.anchorBusy.has(row.runId)) return;
    this.hideMore(row);
    this.element(`#cc-run-${row.runId}-more-btn`)?.focus();
    this.anchorToggle.emit(row);
  }

  private hideMore(row: CcRunRow): void {
    try {
      this.element(`#cc-run-${row.runId}-more`)?.hidePopover();
    } catch {
      // Already hidden.
    }
    if (this.moreOpenId === row.runId) this.moreOpenId = null;
  }

  // --- A card ---

  eligibilityOf(row: CcScopeUnit, axis: CcAxis): CcAxisEligibility | null {
    return row.eligibility.find(entry => entry.axis === axis) ?? null;
  }

  /** The axes a unit cannot be used on, with the server's reason. */
  ineligibleReasons(row: CcScopeUnit): { axis: string; reason: string }[] {
    return row.eligibility
      .filter(entry => !entry.eligible)
      .map(entry => ({ axis: axisText(entry.axis), reason: entry.reason || 'Excluded' }));
  }

  axisLabel(axis: CcAxis): string {
    return axisText(axis);
  }

  started(row: CcScopeUnit): string {
    return formatUtcDateTime(row.startedAtUtc);
  }

  status(row: CcRunRow): string {
    return runStatusText(row.status);
  }

  segment(row: CcRunRow): string {
    return segmentText(row);
  }

  regrade(row: CcRunRow): string {
    return regradeCoverageText(row);
  }

  controls(row: CcRunRow): string {
    return controlRunsText(row);
  }

  served(row: CcRunRow): string {
    return servedModelsText(row.servedModelIds);
  }

  inclusionText(row: CcScopeUnit): string {
    const inclusion = this.inclusionOf(row);
    return inclusion === 'included' ? '' : CC_INCLUSION_TEXT[inclusion];
  }

  /** `Battery run #12 · suite 1 of 2` on a run that is a battery member; empty otherwise. */
  memberTag(row: CcRunRow): string {
    if (row.batteryRunId === null || row.batteryRunId === undefined) return '';
    const position = typeof row.batterySuitePosition === 'number' && typeof row.batterySuiteCount === 'number'
      ? ` · suite ${row.batterySuitePosition} of ${row.batterySuiteCount}`
      : '';
    return `Battery run #${row.batteryRunId}${position}`;
  }

  // --- A battery-run card ---

  batteryStatus(row: CcBatteryRunRow): string {
    return batteryRunStatusText(row.status);
  }

  /** `Harness 54`, `Harnesses 53, 54`, or `Harness —` with none recorded. */
  batteryHarnessText(row: CcBatteryRunRow): string {
    if (row.harnessVersions.length === 0) return 'Harness —';
    return row.harnessVersions.length === 1 ? `Harness ${row.harnessVersions[0]}` : `Harnesses ${row.harnessVersions.join(', ')}`;
  }

  /** `2 of 2 suites`: the suite slots that hold a usable member. */
  batterySuitesText(row: CcBatteryRunRow): string {
    const usable = new Set(row.members.map(member => member.suiteKey || member.suiteName)).size;
    return `${usable} of ${plural(row.suiteCount, 'suite')}`;
  }

  /** `#98 · GnollHack Player Assistance Benchmark Suite · Completed · harness 54`. */
  memberText(member: CcRunRow): string {
    return `#${member.runId} · ${member.suiteName} · ${runStatusText(member.status)} · harness ${member.harnessVersion ?? '—'}`;
  }
}
