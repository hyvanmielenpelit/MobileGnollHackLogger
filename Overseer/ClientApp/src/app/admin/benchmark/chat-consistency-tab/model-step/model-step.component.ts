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
  notAnalyzedRuns,
  scopeChangeCount,
  scopeIsDefault,
  scopeRuns,
  setFirstRun,
  setLastRun,
  toggleLeftOut
} from '../chat-consistency-scope';
import { CC_AXES, CcAxis, CcAxisEligibility, CcModelAxis, CcRunRow, CcTimeline } from '../chat-consistency.models';

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

/** Where the run list's Sort by choice is kept, per browser. */
export const CC_RUNS_VIEW_STORAGE_KEY = 'overseer.benchmark.chatConsistency.runs.view';

/** The orders Sort by offers for the run cards. */
export const CC_RUN_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'started', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'started', direction: 'asc' },
  { id: 'suite', label: 'Suite (A–Z)', column: 'suite', direction: 'asc' },
  { id: 'harness', label: 'Harness', column: 'harness', direction: 'desc' }
];

/** The *In the analysis* facet's values, in order. */
const INCLUSION_ORDER: readonly CcRunInclusion[] = ['included', 'leftOut', 'beforeSpan', 'afterSpan'];

const INCLUSION_LABELS: Readonly<Record<CcRunInclusion, string>> = {
  included: 'Included',
  leftOut: 'Left out',
  beforeSpan: 'Before the first run',
  afterSpan: 'After the last run'
};

/** How many left-out runs the selection band names before *+N more*. */
const BAND_LEFT_OUT_CHIPS = 6;

/** One chip of the selection band: a first or last run mark, or a left-out run. */
export interface CcScopeChip {
  key: string;
  kind: 'first' | 'last' | 'leftOut';
  runId: number;
  label: string;
  removeLabel: string;
  tip: string;
}

/** A harness version's sort value: its leading number, else -1. */
function harnessValue(row: CcRunRow): number {
  const value = Number.parseFloat(row.harnessVersion ?? '');
  return Number.isFinite(value) ? value : -1;
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

/**
 * Step 1 of the Chat Consistency wizard: the subject and the UTC dates in one row, then the runs as a
 * card list with the shared filter bar, and the selection band that chooses which of them the analysis
 * uses: a first and a last run, and runs left out. The filters change what is shown, never the
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
  /** The runs the analysis uses: the first and last run marks and the runs left out. */
  @Input() scope: CcRunScope = CC_EMPTY_SCOPE;
  @Input() timeline: CcTimeline | null = null;
  @Input() rows: readonly CcRunRow[] = [];
  @Input() loading = false;
  @Input() timelineError: string | null = null;
  @Input() runsError: string | null = null;
  /** Runs whose anchor mark is being saved. */
  @Input() anchorBusy: ReadonlySet<number> = new Set<number>();
  @Input() anchorError: string | null = null;
  /** A one-off confirmation for the list's status line. */
  @Input() announcement = '';
  /** Why the model and the dates cannot change now; empty while they can. */
  @Input() lockedReason = '';

  @Output() readonly modelChange = new EventEmitter<string>();
  @Output() readonly rangeChange = new EventEmitter<CcDateRange>();
  @Output() readonly scopeChange = new EventEmitter<CcRunScope>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly anchorToggle = new EventEmitter<CcRunRow>();
  @Output() readonly openRunReport = new EventEmitter<number>();

  readonly axisOrder = CC_AXES;
  readonly rangePresets = CC_RANGE_PRESETS;
  readonly sorts = CC_RUN_SORTS;

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
      { column: 'suite', label: 'Suite', values: row => row.suiteName },
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
    memoDeps: () => [this.scope],
    onChange: () => this.cdr.markForCheck()
  });

  /** The selection derived from the rows and the scope, kept until either object changes. */
  private scopeMemo: {
    rows: readonly CcRunRow[];
    scope: CcRunScope;
    scoped: CcRunRow[];
    inclusion: ReadonlyMap<number, CcRunInclusion>;
    chips: CcScopeChip[];
    moreLeftOut: number;
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
    if (keyChange && !keyChange.firstChange && keyChange.previousValue !== keyChange.currentValue) {
      this.list.reset();
      this.scopeNote = '';
    }
  }

  ngOnDestroy(): void {
    this.list.dispose();
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

  // --- The selection ---

  private scopeState(): NonNullable<CcModelStepComponent['scopeMemo']> {
    const memo = this.scopeMemo;
    if (memo && memo.rows === this.rows && memo.scope === this.scope) return memo;
    const scope = this.scope;
    const chips: CcScopeChip[] = [];
    if (scope.firstRunId !== null) {
      chips.push({
        key: 'first', kind: 'first', runId: scope.firstRunId, label: `First: #${scope.firstRunId}`,
        removeLabel: `Remove the first-run mark from run #${scope.firstRunId}`, tip: 'Remove the first-run mark'
      });
    }
    if (scope.lastRunId !== null) {
      chips.push({
        key: 'last', kind: 'last', runId: scope.lastRunId, label: `Last: #${scope.lastRunId}`,
        removeLabel: `Remove the last-run mark from run #${scope.lastRunId}`, tip: 'Remove the last-run mark'
      });
    }
    const leftOut = [...scope.leftOut].sort((a, b) => a - b);
    for (const runId of leftOut.slice(0, BAND_LEFT_OUT_CHIPS)) {
      chips.push({
        key: `left-${runId}`, kind: 'leftOut', runId, label: `Left out: #${runId}`,
        removeLabel: `Include run #${runId} again`, tip: 'Include again'
      });
    }
    this.scopeMemo = {
      rows: this.rows,
      scope,
      scoped: scopeRuns(this.rows, scope),
      inclusion: notAnalyzedRuns(this.rows, scope),
      chips,
      moreLeftOut: Math.max(0, leftOut.length - BAND_LEFT_OUT_CHIPS)
    };
    return this.scopeMemo;
  }

  inclusionOf(row: CcRunRow): CcRunInclusion {
    return this.scopeState().inclusion.get(row.runId) ?? 'included';
  }

  get scopedRows(): readonly CcRunRow[] {
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

  /**
   * `Runs in the analysis — 15 of 19 runs in these dates · from #21 (2026-09-20) to #93 (2026-10-05) ·
   * 2 left out`, or `… — all 19 runs in these dates` while nothing is chosen.
   */
  get scopeSummary(): string {
    const total = this.rows.length;
    if (this.scopeIsDefault) {
      return total === 1 ? 'Runs in the analysis — the one run in these dates' : `Runs in the analysis — all ${total} runs in these dates`;
    }
    const scoped = this.scopedRows;
    let text = `Runs in the analysis — ${scoped.length} of ${plural(total, 'run')} in these dates`;
    if (scoped.length > 0) {
      const first = scoped[0];
      const last = scoped[scoped.length - 1];
      text += first === last
        ? ` · only #${first.runId} (${formatUtcDate(first.startedAtUtc)})`
        : ` · from #${first.runId} (${formatUtcDate(first.startedAtUtc)}) to #${last.runId} (${formatUtcDate(last.startedAtUtc)})`;
    }
    if (this.scope.leftOut.size > 0) text += ` · ${this.scope.leftOut.size} left out`;
    return text;
  }

  isFirst(row: CcRunRow): boolean {
    return this.scope.firstRunId === row.runId;
  }

  isLast(row: CcRunRow): boolean {
    return this.scope.lastRunId === row.runId;
  }

  /** `Before the first run (#21)`, `After the last run (#93)`; empty for a run inside the span. */
  outReason(row: CcRunRow): string {
    switch (this.inclusionOf(row)) {
      case 'beforeSpan': return `Before the first run (#${this.scope.firstRunId})`;
      case 'afterSpan': return `After the last run (#${this.scope.lastRunId})`;
      default: return '';
    }
  }

  /** The include checkbox: a run before or after the span refuses the click; any other toggles left out. */
  onIncludeClick(row: CcRunRow, event: Event): void {
    if (this.outReason(row)) {
      event.preventDefault();
      return;
    }
    const include = (event.target as HTMLInputElement).checked;
    this.applyScope(toggleLeftOut(this.scope, row.runId, include), '');
  }

  toggleFirst(row: CcRunRow): void {
    const next = setFirstRun(this.scope, this.rows, this.isFirst(row) ? null : row.runId);
    this.applyScope(next.scope, next.note);
  }

  toggleLast(row: CcRunRow): void {
    const next = setLastRun(this.scope, this.rows, this.isLast(row) ? null : row.runId);
    this.applyScope(next.scope, next.note);
  }

  /** Clear selection: every run in the dates again; focus moves to the band's label. */
  clearScope(): void {
    this.applyScope(CC_EMPTY_SCOPE, '');
    this.focusAfterRender(() => this.element('#cc-scope-label'));
  }

  /** Removes one chip, then focuses the chip now in its place, else Clear selection, else the label. */
  removeScopeChip(chip: CcScopeChip, index: number): void {
    let next: CcRunScope;
    switch (chip.kind) {
      case 'first': next = setFirstRun(this.scope, this.rows, null).scope; break;
      case 'last': next = setLastRun(this.scope, this.rows, null).scope; break;
      default: next = toggleLeftOut(this.scope, chip.runId, true);
    }
    this.applyScope(next, '');
    this.focusAfterRender(() => {
      const buttons = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('.cc-scope-chip-remove'));
      return buttons[index] ?? this.element('.cc-scope-clear') ?? this.element('#cc-scope-label');
    });
  }

  /** *+N more*: the list shows the left-out runs. */
  showLeftOut(): void {
    this.list.setFacet('inclusion', ['leftOut']);
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

  get cards(): CcRunRow[] {
    return this.list.view(this.rows);
  }

  get facets(): CardListFacet[] {
    return this.list.facets(this.rows);
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.rows);
  }

  get listStatus(): string {
    return this.list.statusText(this.rows, { one: 'run', many: 'runs' });
  }

  get showFilterBar(): boolean {
    return this.rows.length > 1 || this.table.hasActiveFilters;
  }

  get noMatches(): boolean {
    return this.table.noMatches(this.rows);
  }

  get remainingCount(): number {
    return this.list.remainingCount(this.rows);
  }

  get nextBatchCount(): number {
    return this.list.nextBatchCount(this.rows);
  }

  get matchingCount(): number {
    return this.list.matching(this.rows).length;
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once; in an empty field it passes through. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) {
      this.cdr.detectChanges();
    }
  }

  onSortChange(event: Event): void {
    if (this.list.setSort((event.target as HTMLSelectElement).value)) {
      this.cdr.detectChanges();
    }
  }

  onFacetChange(column: string, values: string[]): void {
    this.list.setFacet(column, values);
    this.cdr.detectChanges();
  }

  /** Removes a filter chip, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.rows);
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.cc-runs-filter-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#cc-runs-search'))?.focus();
  }

  /** Clears the search and every filter, then focuses the search. */
  clearFilters(): void {
    this.list.clearFilters();
    this.cdr.detectChanges();
    this.element('#cc-runs-search')?.focus();
  }

  showMore(): void {
    this.focusCard(this.list.showMore(this.rows));
  }

  showAll(): void {
    this.focusCard(this.list.showAll(this.rows));
  }

  /** Renders, then focuses the title of the card at `index` in the view, if there is one. */
  private focusCard(index: number): void {
    this.cdr.detectChanges();
    refreshAnchorPositioning();
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

  eligibilityOf(row: CcRunRow, axis: CcAxis): CcAxisEligibility | null {
    return row.eligibility.find(entry => entry.axis === axis) ?? null;
  }

  /** The axes a run cannot be used on, with the server's reason. */
  ineligibleReasons(row: CcRunRow): { axis: string; reason: string }[] {
    return row.eligibility
      .filter(entry => !entry.eligible)
      .map(entry => ({ axis: axisText(entry.axis), reason: entry.reason || 'Excluded' }));
  }

  axisLabel(axis: CcAxis): string {
    return axisText(axis);
  }

  started(row: CcRunRow): string {
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

  inclusionText(row: CcRunRow): string {
    const inclusion = this.inclusionOf(row);
    return inclusion === 'included' ? '' : CC_INCLUSION_TEXT[inclusion];
  }
}
