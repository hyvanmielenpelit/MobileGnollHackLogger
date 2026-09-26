import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { copyToClipboard } from '../../../utils/clipboard.util';
import { saveFigureBlob } from './figure-export';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { exactFilter, TableState } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import { ModelIdentityComponent } from '../../../shared/model-identity/model-identity.component';
import type {
  BenchmarkRunGroupDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';
import {
  HASH_SHAPED,
  SHORT_HASH_LENGTH,
  buildConditionLegend,
  conditionDetailFor,
  isGeneratedGroupName,
  selectedConditions,
  sourceLabel,
  summarizeConditionDifference
} from './model-comparison.models';
import type {
  BenchmarkComparabilityConditionDto,
  BenchmarkComparabilityDifferenceDto,
  BenchmarkComparabilityIndexDto,
  BenchmarkComparabilityIndexEntryDto,
  BenchmarkComparabilityKeyValueDto,
  ConditionDetail,
  ConditionDetailRow,
  ConditionDifferenceSummary,
  ConditionLegend,
  ConditionLegendItem,
  ConfigurationField
} from './model-comparison.models';

/** One selectable suite for the scope control. Structural, so any suite DTO with these two fits. */
export interface ModelComparisonSuiteOption {
  readonly id: number;
  readonly name: string;
}

/** The comparability-index key for one run: the format `conditionOf` and `selectedConditions` expect. */
function runKey(run: BenchmarkRunSummaryDto): string {
  return `run:${run.id}`;
}

/** The comparability-index key for one analysis group: the format `conditionOf` and `selectedConditions` expect. */
function groupKey(group: BenchmarkRunGroupDto): string {
  return `group:${group.id}`;
}

/** The two source lists, emitted together because a comparison is made of both at once. */
export interface ModelComparisonSelection {
  readonly runIds: number[];
  readonly groupIds: number[];
}

/**
 * The most sources one request may carry.
 *
 * Well above the eight the figures plot, because the extras are legitimate — they stay in the entry
 * table and the view names them — but not unbounded: a comparison over every stored run is a slow
 * query whose figure no reader can use.
 */
export const MAX_COMPARISON_SOURCES = 24;

/**
 * The two source sections' titles.
 *
 * Exported because the wizard's notice band names the table its notices report on, and a second
 * copy of either string would go stale the first time one is renamed. The `<h4>` elements below
 * bind to these, so the heading and the label are the same value.
 */
export const RUN_SECTION_TITLE = 'Single runs';
export const GROUP_SECTION_TITLE = 'Analysis groups';

/** The canonical rendering of an absent value, as `BenchmarkComparabilityKey.NoValue` writes it. */
const NO_VALUE = '(none)';

/** Other conditions the legend adds per Show more; the charted one is always shown besides. */
export const CONDITION_PAGE_SIZE = 5;

/** Member labels a legend item names before the rest are counted instead. */
const MEMBER_SUMMARY_LABELS = 6;

/** How long a copy result stays on the status line before it clears itself. */
const COPY_STATE_MS = 2000;

/** Both copy controls report the same fallback, because the remedy is the same one. */
const COPY_FAILED = 'Copy failed — select the text instead.';

/**
 * One kind of must-match key, with the heading and the sentence that say what that kind governs.
 *
 * The must-match set spans two kinds in the server's own taxonomy, and separating *what was asked*
 * from *how it was asked and graded* is what turns a list of internal key names into something a
 * reader can cite. The kinds are listed in reading order; a kind the server sends that is not
 * named here renders after these, under its own taxonomy name.
 */
const METHODS_KINDS: readonly { kind: string; title: string; note: string }[] = [
  {
    kind: 'Fundamental',
    title: 'The exam',
    note: 'What was asked, and which revision of the answer key graded it.'
  },
  {
    kind: 'Instrument',
    title: 'The apparatus',
    note: 'How it was asked and how it was graded.'
  }
];

/** The lead sentence for a taxonomy kind {@link METHODS_KINDS} does not name. */
const METHODS_KIND_FALLBACK_NOTE = 'Other values every source in this condition agrees on.';

/** One kind's worth of the methods block: its own heading, its lead sentence, and its keys. */
export interface MethodsKindGroup {
  readonly kind: string;
  readonly title: string;
  readonly note: string;
  readonly keys: readonly BenchmarkComparabilityKeyValueDto[];
}

/**
 * The taxonomy kind as an operator reads it, beside the hue the badge carries it in.
 *
 * The word is the carrier and the hue only reinforces it, so a kind the server adds renders under
 * its own name in the neutral treatment rather than disappearing into an unlabelled colour.
 */
const CONDITION_KIND_LABELS: Readonly<Record<string, string | undefined>> = {
  Fundamental: 'Fundamental',
  Candidate: 'Candidate',
  Instrument: 'Instrument',
  SpeedAndCost: 'Speed and cost'
};

/** Characters of an unparsed value that render before the collapsed box and its control take over. */
const LONG_VALUE_CHARS = 600;

/**
 * One side of a difference, so both panels of a row render from one block of markup.
 *
 * Absence is carried by its own flag rather than by a null value: the template branches on
 * `attributed` and `hasFields` and never on the shape of `value`, so one markup block covers a
 * parsed configuration, a digest, a long raw value and a side nothing could be attributed to.
 */
export interface ConditionDetailSide {
  /** Part of the DOM id of the row's expand control, and of nothing else. */
  readonly side: 'this' | 'reference';
  readonly title: string;
  readonly value: string;
  /** False when no variant could be attributed to this side; the row lists them all instead. */
  readonly attributed: boolean;
  readonly fields: readonly ConfigurationField[];
  readonly hasFields: boolean;
}

/** One panel of a difference row, with absence folded into its own flags. */
function conditionSide(
  side: 'this' | 'reference',
  title: string,
  value: string | null,
  fields: ConfigurationField[] | null
): ConditionDetailSide {
  return {
    side,
    title,
    value: value ?? '',
    attributed: value != null,
    fields: fields ?? [],
    hasFields: fields != null
  };
}

/** One differing key of a legend item, as its body lists it. */
export interface ConditionDifferenceLine {
  readonly name: string;
  readonly label: string;
  readonly summary: ConditionDifferenceSummary;
}

/** The hue class of a condition badge, keyed by ordinal so same-condition rows read alike. */
function conditionHueClass(ordinal: number | null): string {
  return ordinal == null
    ? 'csp-condition csp-condition-none'
    : `csp-condition csp-condition-${((ordinal - 1) % 3) + 1}`;
}

/**
 * Chooses what a cross-model comparison is computed over: single runs at R = 1, analysis groups at
 * one pooled point each, and the suite scope that decides which of either are offered.
 *
 * Presentational, like the view it feeds: it owns no fetching and mutates nothing but its own table
 * state. The host holds the selection and issues the request, so the two panels of this sub-tab
 * cannot disagree about what is selected.
 *
 * The division of labour with `ModelComparisonComponent` is by **stage**, not by kind of control.
 * Everything that decides which sources are in the request at all belongs here; everything that
 * decides how the returned set is drawn belongs in that component's filter row. Suite scope is a
 * selection-stage control — its whole effect is visible in the two tables below it — so it sits
 * here, next to what it governs, rather than in a row otherwise made of chart options.
 */
@Component({
  selector: 'app-comparison-source-picker',
  standalone: true,
  imports: [CommonModule, FormsModule, SortHeaderComponent, TablePagerComponent, ModelIdentityComponent],
  templateUrl: './comparison-source-picker.component.html',
  styleUrls: ['./comparison-source-picker.component.scss']
})
export class ComparisonSourcePickerComponent implements OnInit, OnDestroy {
  /** Protected rather than private: the filter rows call it directly after a `TableState` mutation. */
  protected cdr = inject(ChangeDetectorRef);

  /** Runs offered as R = 1 points. Already scoped to `suiteId` by the host. */
  @Input() runs: readonly BenchmarkRunSummaryDto[] = [];

  /** Analysis groups offered as one pooled point each. Already scoped to `suiteId` by the host. */
  @Input() groups: readonly BenchmarkRunGroupDto[] = [];

  /** Suites the scope control offers. Empty renders it disabled rather than absent. */
  @Input() suites: readonly ModelComparisonSuiteOption[] = [];

  /** The active suite scope; null offers every suite. */
  @Input() suiteId: number | null = null;

  @Input() selectedRunIds: readonly number[] = [];

  @Input() selectedGroupIds: readonly number[] = [];

  /** A request is in flight. Compare is disabled rather than queued behind it. */
  @Input() loading = false;

  /** Which condition each offered run and group falls into, and what it differs on outside it. */
  @Input() comparabilityIndex: BenchmarkComparabilityIndexDto | null = null;

  /** The index is being (re)computed. The condition column and filter go muted rather than stale. */
  @Input() indexLoading = false;

  @Output() suiteIdChange = new EventEmitter<number | null>();
  @Output() selectionChange = new EventEmitter<ModelComparisonSelection>();

  /** The hard cap, exposed so the template names the same number the guard enforces. */
  readonly maxSources = MAX_COMPARISON_SOURCES;

  /** The two source-kind tabs, in display order. */
  readonly sourceTabs = ['runs', 'groups'] as const;

  activeSourceTab: 'runs' | 'groups' = 'runs';

  readonly runTable = new TableState<BenchmarkRunSummaryDto>('id', 'desc').registerAccessors(
    {
      selected: r => (this.isRunSelected(r.id) ? 1 : 0),
      id: r => r.id,
      suiteName: r => r.suiteName,
      testedModel: r => this.runModelText(r),
      status: r => this.runStatus(r),
      qualityIndex: r => r.qualityIndex ?? r.finalScore,
      startedAtUtc: r => new Date(r.startedAtUtc),
      // Unassigned and self-inconsistent sources sort last under an ascending sort.
      condition: r => this.conditionOrdinal(runKey(r)) ?? Number.MAX_SAFE_INTEGER
    },
    {
      suiteName: r => r.suiteName,
      testedModel: r => this.runModelText(r),
      status: exactFilter(r => this.runStatus(r)),
      selected: exactFilter(r => (this.isRunSelected(r.id) ? 'yes' : 'no')),
      condition: exactFilter(r => this.conditionLabel(runKey(r)))
    }
  );

  readonly groupTable = new TableState<BenchmarkRunGroupDto>('createdAtUtc', 'desc').registerAccessors(
    {
      selected: g => (this.isGroupSelected(g.id) ? 1 : 0),
      id: g => g.id,
      testedModel: g => this.groupModelText(g),
      suiteName: g => g.suiteName,
      tier: g => g.tierLabel || String(g.tier),
      runCount: g => g.runCount,
      createdAtUtc: g => new Date(g.createdAtUtc),
      analysis: g => this.analysisState(g),
      // Unassigned and self-inconsistent sources sort last under an ascending sort.
      condition: g => this.conditionOrdinal(groupKey(g)) ?? Number.MAX_SAFE_INTEGER
    },
    {
      // The name stays searchable here even where the row does not render it.
      testedModel: g => `${this.groupModelText(g)} ${g.name}`,
      suiteName: g => g.suiteName,
      tier: exactFilter(g => String(g.tier)),
      selected: exactFilter(g => (this.isGroupSelected(g.id) ? 'yes' : 'no')),
      condition: exactFilter(g => this.conditionLabel(groupKey(g)))
    }
  );

  ngOnInit(): void {
    // Every icon-only control below carries an interestfor tooltip, and the primitives behind those
    // are not baseline everywhere.
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.clearCopyStateTimer();
    this.clearConditionCopyTimer();
  }

  // ---------------------------------------------------------------------------------------------
  // The source-kind tabs
  // ---------------------------------------------------------------------------------------------

  selectSourceTab(tab: 'runs' | 'groups'): void {
    this.activeSourceTab = tab;
    this.cdr.detectChanges();
  }

  /**
   * Left/Right move and wrap, Home/End jump to the ends. Focus follows selection in the same
   * turn, or the tab holding focus becomes `tabindex="-1"` and the next Tab press jumps
   * somewhere unexpected.
   */
  onSourceTabKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.sourceTabs.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }

    event.preventDefault();
    const next = (requested + this.sourceTabs.length) % this.sourceTabs.length;
    const tab = this.sourceTabs[next];
    this.selectSourceTab(tab);
    document.getElementById('csp-src-tab-' + tab)?.focus();
  }

  /** The section title a tab names, exported so the wizard's notice band can cite the same string. */
  sourceTabTitle(tab: 'runs' | 'groups'): string {
    return tab === 'runs' ? RUN_SECTION_TITLE : GROUP_SECTION_TITLE;
  }

  /** The tab's row count — what its table actually lists, unselectable rows included. */
  sourceTabRowCount(tab: 'runs' | 'groups'): number {
    return tab === 'runs' ? this.runs.length : this.groups.length;
  }

  /** The noun after the tab's row count, singular for one. */
  sourceTabNoun(tab: 'runs' | 'groups'): string {
    const singular = tab === 'runs' ? 'run' : 'group';
    return this.sourceTabRowCount(tab) === 1 ? singular : `${singular}s`;
  }

  /** The tab's own selected count, so each tab's badge names only its own kind. */
  sourceTabSelectedCount(tab: 'runs' | 'groups'): number {
    return tab === 'runs' ? this.selectedRunCount : this.selectedGroupCount;
  }

  get selectedRunCount(): number {
    return this.selectedRunIds.length;
  }

  get selectedGroupCount(): number {
    return this.selectedGroupIds.length;
  }

  // ---------------------------------------------------------------------------------------------
  // Suite scope
  // ---------------------------------------------------------------------------------------------

  onSuiteChange(value: number | null): void {
    this.suiteIdChange.emit(value == null || !Number.isFinite(value) ? null : value);
  }

  // ---------------------------------------------------------------------------------------------
  // Selection
  //
  // Held by id throughout, so a tick survives paging, filtering and a suite change that keeps the
  // row in scope. Every mutation emits the whole selection: the host is the owner, and a picker
  // that kept a private copy would drift from it the first time the host dropped an id.
  // ---------------------------------------------------------------------------------------------

  isRunSelected(id: number): boolean {
    return this.selectedRunIds.includes(id);
  }

  isGroupSelected(id: number): boolean {
    return this.selectedGroupIds.includes(id);
  }

  toggleRun(run: BenchmarkRunSummaryDto): void {
    if (!this.isRunSelectable(run)) {
      return;
    }
    const runIds = this.isRunSelected(run.id)
      ? this.selectedRunIds.filter(id => id !== run.id)
      : [...this.selectedRunIds, run.id];
    this.emitSelection(runIds, [...this.selectedGroupIds]);
  }

  toggleGroup(group: BenchmarkRunGroupDto): void {
    const groupIds = this.isGroupSelected(group.id)
      ? this.selectedGroupIds.filter(id => id !== group.id)
      : [...this.selectedGroupIds, group.id];
    this.emitSelection([...this.selectedRunIds], groupIds);
  }

  get selectedCount(): number {
    return this.selectedRunIds.length + this.selectedGroupIds.length;
  }

  /** Selected runs the current page of the run table does not show. */
  get offPageRunCount(): number {
    const onPage = new Set(this.runTable.view(this.runs).map(run => run.id));
    return this.selectedRunIds.filter(id => !onPage.has(id)).length;
  }

  /** Selected groups the current page of the group table does not show. */
  get offPageGroupCount(): number {
    const onPage = new Set(this.groupTable.view(this.groups).map(group => group.id));
    return this.selectedGroupIds.filter(id => !onPage.has(id)).length;
  }

  get showSelectedRunsOnly(): boolean {
    return this.runTable.filters['selected'] === 'yes';
  }

  get showSelectedGroupsOnly(): boolean {
    return this.groupTable.filters['selected'] === 'yes';
  }

  toggleShowSelectedRunsOnly(): void {
    this.runTable.setFilter('selected', this.showSelectedRunsOnly ? '' : 'yes');
    this.cdr.detectChanges();
  }

  toggleShowSelectedGroupsOnly(): void {
    this.groupTable.setFilter('selected', this.showSelectedGroupsOnly ? '' : 'yes');
    this.cdr.detectChanges();
  }

  // ---------------------------------------------------------------------------------------------
  // The comparability index
  //
  // Only the largest condition may be charted; everything outside it is excluded from the
  // comparison. This surfaces that fact in the picker rather than leaving it to be discovered in
  // the result: a condition badge and detail per row, and a filter to narrow to one condition.
  // ---------------------------------------------------------------------------------------------

  private legendCache: {
    readonly index: BenchmarkComparabilityIndexDto | null;
    readonly legend: ConditionLegend;
  } | null = null;

  /**
   * The index's legend view and entry map, rebuilt only when the index object is replaced.
   *
   * Keyed on identity rather than on `ngOnChanges`, which a directly assigned input never fires.
   * The host always replaces the index and never mutates it in place.
   */
  get legend(): ConditionLegend {
    const index = this.comparabilityIndex;
    if (this.legendCache?.index !== index) {
      this.legendCache = { index, legend: buildConditionLegend(index) };
      this.differenceCache.clear();
    }
    return this.legendCache!.legend;
  }

  private comparabilityEntry(key: string): BenchmarkComparabilityIndexEntryDto | undefined {
    return this.legend.entryByKey.get(key);
  }

  /** The row's condition label, or a muted dash while the index has not loaded or lacks the key. */
  conditionLabel(key: string): string {
    if (this.indexLoading || !this.comparabilityIndex) {
      return '—';
    }
    return this.comparabilityEntry(key)?.conditionLabel ?? '—';
  }

  /** The row's condition ordinal, or null while the index has not loaded, lacks the key or leaves it unassigned. */
  conditionOrdinal(key: string): number | null {
    const ordinal = this.comparabilityEntry(key)?.conditionOrdinal ?? 0;
    return ordinal === 0 ? null : ordinal;
  }

  conditionDifferences(key: string): BenchmarkComparabilityDifferenceDto[] {
    return this.comparabilityEntry(key)?.differencesFromLargest ?? [];
  }

  /**
   * Whether the row has anything to say beyond its badge: a self-inconsistent source, or one that
   * differs from the largest condition. A source inside the largest condition offers no control,
   * because the dialog would open on an empty table.
   */
  hasConditionDetail(key: string): boolean {
    const entry = this.comparabilityEntry(key);
    return entry != null && (entry.selfInconsistent || entry.differencesFromLargest.length > 0);
  }

  /** A DOM id and anchor name derived from an entry key, which carries a `run:12` style colon. */
  conditionTipId(key: string): string {
    return `csp-tip-cond-${key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  /** One of a small hue palette, keyed by condition ordinal so same-condition rows read alike. */
  conditionBadgeClass(key: string): string {
    return conditionHueClass(this.conditionOrdinal(key));
  }

  // ---------------------------------------------------------------------------------------------
  // The condition detail dialog
  //
  // A badge says which cohort a source is in; this says why. One row per must-match key it differs
  // on, both values side by side, and — where the value is a `key=value;…` configuration — the one
  // field that moved rather than two long strings for the reader to diff by eye.
  //
  // A dialog rather than the hint tooltip it replaces: the values run to serialized options blobs
  // and 64-character digests, a tooltip cannot be scrolled, selected or copied from, and the whole
  // point of the detail is that it can be pasted into a report beside the figure it explains.
  // ---------------------------------------------------------------------------------------------

  @ViewChild('conditionDetailDialog')
  private conditionDetailDialogRef?: ElementRef<HTMLDialogElement>;

  /** What the dialog is showing. Kept after a close so the exit transition has content to draw. */
  conditionDetail: ConditionDetail | null = null;

  /** The result of the last copy made from the dialog, on its own line and its own timer. */
  conditionCopyState = '';

  private conditionCopyTimer: ReturnType<typeof setTimeout> | null = null;

  /** The button the dialog was opened from, so focus returns where the reader left it. */
  private conditionDetailTrigger: HTMLElement | null = null;

  /** Which long values the reader has expanded, by {@link conditionValueId}. */
  private readonly expandedConditionValues = new Set<string>();

  openConditionDetail(key: string, event?: Event): void {
    this.conditionDetail = conditionDetailFor(this.comparabilityIndex, key, this.sourceRunIds(key));
    this.conditionDetailTrigger = (event?.currentTarget as HTMLElement | null) ?? null;
    this.conditionCopyState = '';
    this.expandedConditionValues.clear();
    // The body is autofocused, so it has to hold this source's rows before the dialog is promoted
    // to the top layer rather than the previous source's.
    this.cdr.detectChanges();
    this.conditionDetailDialogRef?.nativeElement.showModal();
  }

  /**
   * The runs behind one source, which is what attributes a differing key's value to it.
   *
   * A run is its own single run. A group's membership is only present when the group was fetched
   * individually — the list endpoint this picker is fed from sends none — so this is empty for a
   * group in practice, and the detail falls back to listing every variant with its runs.
   */
  private sourceRunIds(key: string): number[] {
    const entry = this.comparabilityEntry(key);
    if (entry == null) {
      return [];
    }
    if (entry.sourceKind === 'Group') {
      const group = this.groups.find(candidate => candidate.id === entry.sourceId);
      return (group?.members ?? []).map(member => member.runId);
    }
    return [entry.sourceId];
  }

  /** "Run 46 — Condition E": the source as an operator names it, and the cohort it fell into. */
  get conditionDetailTitle(): string {
    const detail = this.conditionDetail;
    return detail == null
      ? 'Comparability detail'
      : `${detail.sourceLabel} — ${detail.conditionLabel}`;
  }

  /** The two panels of one row, in reading order. */
  conditionSides(row: ConditionDetailRow): ConditionDetailSide[] {
    return [
      conditionSide('this', 'This source', row.thisValue, row.thisFields),
      conditionSide('reference', 'Reference', row.referenceValue, row.referenceFields)
    ];
  }

  conditionKindLabel(kind: string): string {
    return CONDITION_KIND_LABELS[kind] ?? kind;
  }

  /** A hue per kind. The badge's text carries the kind; this only reinforces it. */
  conditionKindClass(kind: string): string {
    return `csp-kind csp-kind-${(CONDITION_KIND_LABELS[kind] ? kind : 'Other').toLowerCase()}`;
  }

  /** A DOM id for one panel of one row, unique within the dialog. */
  conditionValueId(row: ConditionDetailRow, side: ConditionDetailSide): string {
    return `csp-cond-${row.name.replace(/[^A-Za-z0-9_-]/g, '-')}-${side.side}`;
  }

  /** A digest renders abbreviated, with the copy control writing it whole. */
  isHashValue(row: ConditionDetailRow, value: string): boolean {
    return row.valueKind === 'Hash' || HASH_SHAPED.test(value.trim());
  }

  isLongValue(value: string): boolean {
    return value.length > LONG_VALUE_CHARS;
  }

  isConditionValueExpanded(row: ConditionDetailRow, side: ConditionDetailSide): boolean {
    return this.expandedConditionValues.has(this.conditionValueId(row, side));
  }

  toggleConditionValue(row: ConditionDetailRow, side: ConditionDetailSide): void {
    const id = this.conditionValueId(row, side);
    if (!this.expandedConditionValues.delete(id)) {
      this.expandedConditionValues.add(id);
    }
    this.cdr.detectChanges();
  }

  isChangedField(row: ConditionDetailRow, name: string): boolean {
    return row.changedFields.includes(name);
  }

  /**
   * The detail as Markdown, values in full.
   *
   * The abbreviations and the collapsed boxes are screen affordances; what gets pasted into a
   * report or a bug is the evidence someone else verifies the figure against.
   */
  conditionDetailMarkdown(): string {
    const detail = this.conditionDetail;
    if (detail == null) {
      return '';
    }
    const lines = [`${detail.sourceLabel} — ${detail.conditionLabel}`, ''];
    if (detail.selfInconsistent) {
      lines.push(detail.selfInconsistentKeys.length > 0
        ? `Its own members disagree on ${detail.selfInconsistentKeys.join(', ')}, so it is not one point.`
        : 'Its own members disagree, so it is not one point.');
      return lines.join('\n');
    }

    lines.push(`Differences from the reference condition, ${detail.referenceConditionLabel}:`);
    lines.push('');
    lines.push('| Key | Kind | This source | Reference |');
    lines.push('| --- | --- | --- | --- |');
    for (const row of detail.rows) {
      lines.push(`| ${row.label} (\`${row.name}\`) | ${this.conditionKindLabel(row.kind)} `
        + `| ${this.markdownCell(row.thisValue)} | ${this.markdownCell(row.referenceValue)} |`);
    }

    const unattributed = detail.rows.filter(row => row.variants.length > 0);
    if (unattributed.length > 0) {
      lines.push('');
      lines.push('Values no side could be attributed to:');
      for (const row of unattributed) {
        for (const variant of row.variants) {
          const runs = variant.runIds.length > 0 ? variant.runIds.join(', ') : 'none recorded';
          lines.push(`- ${row.label}: ${variant.value} (runs ${runs})`);
        }
      }
    }
    return lines.join('\n');
  }

  /** One table cell: the value whole, pipes escaped and newlines flattened so a row stays a row. */
  private markdownCell(value: string | null): string {
    return value == null ? '—' : value.replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
  }

  async copyConditionDetail(): Promise<void> {
    const copied = await copyToClipboard(this.conditionDetailMarkdown());
    this.setConditionCopyState(copied ? 'Comparability detail copied.' : COPY_FAILED);
  }

  /** Saves the same Markdown the copy button puts on the clipboard, named after the source. */
  downloadConditionDetail(): void {
    const detail = this.conditionDetail;
    const text = this.conditionDetailMarkdown();
    if (detail == null || text === '') {
      return;
    }
    const safe = detail.sourceLabel.replace(/[^A-Za-z0-9._-]+/g, '-').replace(/^-+|-+$/g, '').toLowerCase();
    const filename = `comparability_${safe || 'source'}_${this.timestamp()}.md`;
    saveFigureBlob(new Blob([text], { type: 'text/markdown;charset=utf-8' }), filename);
    this.setConditionCopyState(`Saved as ${filename}.`);
  }

  /** `yyyyMMdd_HHmmss`, local time — the same stamp `figureExportFilename` writes in `figure-export.ts`. */
  private timestamp(now: Date = new Date()): string {
    const pad = (value: number): string => String(value).padStart(2, '0');
    return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_`
      + `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  }

  /** Copies one abbreviated value in full, never the twelve characters the row shows. */
  async copyConditionValue(row: ConditionDetailRow, value: string): Promise<void> {
    const copied = await copyToClipboard(value);
    this.setConditionCopyState(copied ? `Full ${row.label} value copied.` : COPY_FAILED);
  }

  private setConditionCopyState(message: string): void {
    this.clearConditionCopyTimer();
    this.conditionCopyState = message;
    this.cdr.markForCheck();
    this.conditionCopyTimer = setTimeout(() => {
      this.conditionCopyTimer = null;
      this.conditionCopyState = '';
      this.cdr.markForCheck();
    }, COPY_STATE_MS);
  }

  private clearConditionCopyTimer(): void {
    if (this.conditionCopyTimer != null) {
      clearTimeout(this.conditionCopyTimer);
      this.conditionCopyTimer = null;
    }
  }

  /**
   * Keeps the dialog's own close and cancel events off the wizard dialog that contains it, and
   * returns focus to the row control the reader opened it from.
   *
   * Escape fires `cancel` and then `close`; only the second is acted on, because the dialog is
   * still modal during the first and every element outside it is still inert. `cancel` is never
   * prevented — a close request the dialog refuses to honour is a trapped reader.
   */
  onConditionDialogClose(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close') {
      return;
    }
    const trigger = this.conditionDetailTrigger;
    this.conditionDetailTrigger = null;
    if (trigger?.isConnected) {
      trigger.focus();
      return;
    }
    // The row has been paged, filtered or refreshed away; the kind tab it was opened from is the
    // nearest place a reader can carry on from.
    const tabId = this.conditionDetail?.sourceKind === 'Group'
      ? 'csp-src-tab-groups'
      : 'csp-src-tab-runs';
    document.getElementById(tabId)?.focus();
  }

  // ---------------------------------------------------------------------------------------------
  // The reference condition, as a methods statement
  //
  // A cross-model chart is a measurement, and a measurement is uninterpretable without the
  // instrument it was taken with. This block is where the reference cohort's instrument is read
  // off: the suite and its item revisions, the candidate prompt configuration, the system prompt,
  // the tool guides, the knowledge base head, the harness, the scoring regime and the budgets.
  // Two charts a month apart are comparable only if those agree, and there is nowhere else in the
  // product they can be compared.
  //
  // Everything below is presentation of what the index already sent. No value is recomputed, and
  // no value is abbreviated anywhere a copy control can reach it.
  // ---------------------------------------------------------------------------------------------

  /** The result of the last copy, cleared after {@link COPY_STATE_MS}. Empty renders nothing. */
  methodsCopyState = '';

  private copyStateTimer: ReturnType<typeof setTimeout> | null = null;

  /** The condition the figures are measured under: ordinal 1, or the first one on offer. */
  get referenceCondition(): BenchmarkComparabilityConditionDto | null {
    return this.legend.reference?.condition ?? null;
  }

  /** Its label, so the block's heading names the same badge the two tables show on its rows. */
  get referenceConditionLabel(): string {
    return this.referenceCondition?.label || 'Condition A';
  }

  /**
   * The reference condition's keys grouped by what they govern, in {@link METHODS_KINDS} order.
   *
   * A kind with no keys in this condition is omitted rather than rendered empty, and a kind the
   * server sends that is not named locally comes last under its own name — so a key added to the
   * taxonomy appears in the block the moment it exists.
   */
  get methodsGroups(): MethodsKindGroup[] {
    const legend = this.legend;
    if (this.methodsCache?.legend !== legend) {
      this.methodsCache = { legend, groups: this.buildMethodsGroups() };
    }
    return this.methodsCache.groups;
  }

  private methodsCache: { readonly legend: ConditionLegend; readonly groups: MethodsKindGroup[] } | null = null;

  private buildMethodsGroups(): MethodsKindGroup[] {
    const keys = this.comparabilityIndex?.largestConditionKeys ?? [];
    const named = METHODS_KINDS.map(kind => kind.kind);
    const order = [...named, ...keys.map(key => key.kind).filter(kind => !named.includes(kind))];

    const groups: MethodsKindGroup[] = [];
    for (const kind of order) {
      if (groups.some(group => group.kind === kind)) {
        continue;
      }
      const inKind = keys.filter(key => key.kind === kind);
      if (inKind.length === 0) {
        continue;
      }
      const known = METHODS_KINDS.find(candidate => candidate.kind === kind);
      groups.push({
        kind,
        title: known?.title ?? kind,
        note: known?.note ?? METHODS_KIND_FALLBACK_NOTE,
        keys: inKind
      });
    }
    return groups;
  }

  /** The first {@link SHORT_HASH_LENGTH} characters: short enough to read, enough to cite. */
  shortHash(value: string | null | undefined): string {
    const text = (value ?? '').trim();
    return text === '' ? '—' : text.slice(0, SHORT_HASH_LENGTH);
  }

  /**
   * A JSON value indented for reading, or the value verbatim when it does not parse.
   *
   * A stored options blob that is not valid JSON still has to render: the operator looking at this
   * block may well be looking at it *because* a run's configuration is malformed.
   */
  formatJson(value: string): string {
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }

  /**
   * One signature split into its settings. A comma-joined string is a single token the browser
   * breaks wherever it likes; one chip per element breaks where a reader expects it.
   */
  splitList(value: string): string[] {
    return value
      .split(/[,;]/)
      .map(part => part.trim())
      .filter(part => part !== '');
  }

  /** "1 source" / "3 sources": a count beside an always-plural noun reads as a rendering fault. */
  countLabel(count: number, singular: string): string {
    return `${count} ${count === 1 ? singular : `${singular}s`}`;
  }

  /** The absence of a value, which must not render as the literal word the wire uses for it. */
  isNoValue(value: string | null | undefined): boolean {
    const text = (value ?? '').trim();
    return text === '' || text === NO_VALUE;
  }

  /** What a `Text` or `Identifier` row shows: the friendlier rendering where the server knows one. */
  displayValue(key: BenchmarkComparabilityKeyValueDto): string {
    return key.displayValue?.trim() || key.value;
  }

  /**
   * The plain-text methods block, which is the whole point of the section: a report, a plan or a
   * bug report needs the instrument pasted into it.
   *
   * Full values throughout, the signature included — an abbreviation is a screen affordance, and a
   * pasted statement is the artifact someone later verifies a figure against.
   */
  methodsStatement(): string {
    const condition = this.referenceCondition;
    const lines: string[] = [];

    const size = `${this.countLabel(condition?.sourceCount ?? 0, 'source')}, `
      + `${this.countLabel(condition?.runCount ?? 0, 'run')}; `
      + `newest run ${this.formatDate(condition?.newestRunStartedAtUtc)}`;
    lines.push(`Reference condition: ${this.referenceConditionLabel} (${size})`);
    lines.push(`Signature: ${condition?.signature || '(none)'}`);
    lines.push(`Selected by: ${this.comparabilityIndex?.referenceSelectionRule ?? ''}`.trimEnd());

    for (const group of this.methodsGroups) {
      lines.push('');
      lines.push(group.title);
      for (const key of group.keys) {
        lines.push(`  ${key.label} (${key.name}): ${this.displayValue(key)}`);
      }
    }
    return lines.join('\n');
  }

  async copyMethodsStatement(): Promise<void> {
    const copied = await copyToClipboard(this.methodsStatement());
    this.setCopyState(copied ? 'Methods statement copied.' : COPY_FAILED);
  }

  /** Copies one key's value in full, never the abbreviation the row shows. */
  async copyFullValue(key: BenchmarkComparabilityKeyValueDto): Promise<void> {
    const copied = await copyToClipboard(key.value);
    this.setCopyState(copied ? `Full ${key.label} value copied.` : COPY_FAILED);
  }

  /** A DOM id derived from a key name, for the disclosure and the tooltip of that row. */
  methodsRowId(key: BenchmarkComparabilityKeyValueDto): string {
    return `csp-methods-${key.name.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  private setCopyState(message: string): void {
    this.clearCopyStateTimer();
    this.methodsCopyState = message;
    this.cdr.markForCheck();
    this.copyStateTimer = setTimeout(() => {
      this.copyStateTimer = null;
      this.methodsCopyState = '';
      this.cdr.markForCheck();
    }, COPY_STATE_MS);
  }

  private clearCopyStateTimer(): void {
    if (this.copyStateTimer != null) {
      clearTimeout(this.copyStateTimer);
      this.copyStateTimer = null;
    }
  }

  /** Copies the condition's signature in full, never the twelve characters the facts row shows. */
  async copySignature(condition: BenchmarkComparabilityConditionDto): Promise<void> {
    const copied = await copyToClipboard(condition.signature);
    this.setCopyState(copied ? `Full ${condition.label} signature copied.` : COPY_FAILED);
  }

  // ---------------------------------------------------------------------------------------------
  // The About conditions dialog
  //
  // Nothing of its body is rendered while it is closed, and inside it only the open accordion
  // item's body is: the legend can list every condition of the index, and each body carries the
  // full methods block or a difference list.
  // ---------------------------------------------------------------------------------------------

  @ViewChild('legendDialog')
  private legendDialogRef?: ElementRef<HTMLDialogElement>;

  /** Whether the dialog is open, which is what renders its body at all. */
  legendOpen = false;

  /** The one accordion item whose body is rendered, by condition ordinal. */
  openConditionOrdinal: number | null = null;

  /** Other conditions currently listed; the charted one is always listed besides. */
  visibleOtherCount = CONDITION_PAGE_SIZE;

  /** Machine key names, key descriptions and full digests, dialog-wide. */
  showTechnicalDetails = false;

  /** Each rendered item's difference lines by ordinal, for the current index. */
  private readonly differenceCache = new Map<number, ConditionDifferenceLine[]>();

  /** Opens the dialog on the charted condition, first page, technical details off. */
  openLegend(): void {
    this.visibleOtherCount = CONDITION_PAGE_SIZE;
    this.showTechnicalDetails = false;
    this.openConditionOrdinal = this.legend.reference?.condition.ordinal ?? null;
    this.legendOpen = true;
    // The body is autofocused, so it has to exist before the dialog is promoted to the top layer.
    this.cdr.detectChanges();
    this.legendDialogRef?.nativeElement.showModal();
  }

  /**
   * Keeps the legend's own close and cancel events off the wizard dialog that contains it — the
   * host closes the wizard from those two events, and this dialog is a descendant of it — and on
   * close drops the body and returns focus to the button that opened it.
   *
   * Only `close` is acted on, for the reason {@link onConditionDialogClose} gives.
   */
  onLegendDialogClose(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close') {
      return;
    }
    this.legendOpen = false;
    this.cdr.detectChanges();
    document.getElementById('csp-legend-trigger')?.focus();
  }

  /**
   * Tracks the exclusive accordion. The closing item's event can arrive before or after the
   * opening one's, so a close clears the state only while it still names that item.
   */
  onConditionToggle(ordinal: number, event: ToggleEvent): void {
    if (event.newState === 'open') {
      this.openConditionOrdinal = ordinal;
    } else if (this.openConditionOrdinal === ordinal) {
      this.openConditionOrdinal = null;
    }
    this.cdr.detectChanges();
  }

  /**
   * The three kinds of key as plain-language cards, each with its technical names. Read only while
   * the dialog is open, which is the only time its body renders.
   */
  get ruleCards(): { id: string; title: string; note: string; names: readonly string[] }[] {
    const index = this.comparabilityIndex;
    return [
      {
        id: 'must-match',
        title: 'Must be the same',
        note: 'If one of these differs, the run is left out of the charts.',
        names: index?.mustMatchKeyNames ?? []
      },
      {
        id: 'model-axis',
        title: 'What you compare',
        note: 'The model and how it is set up — differences here are the point of the comparison.',
        names: index?.modelAxisKeyNames ?? []
      },
      {
        id: 'degrading',
        title: 'May differ, flagged',
        note: 'Still charted; the speed or cost axis they affect is marked.',
        names: index?.degradingKeyNames ?? []
      }
    ];
  }

  /** The accordion's items in order: the charted condition, then the listed page of the others. */
  get listedConditions(): readonly ConditionLegendItem[] {
    const reference = this.legend.reference;
    return reference ? [reference, ...this.visibleOthers] : this.visibleOthers;
  }

  get visibleOthers(): readonly ConditionLegendItem[] {
    return this.legend.others.slice(0, this.visibleOtherCount);
  }

  get remainingOtherCount(): number {
    return Math.max(0, this.legend.others.length - this.visibleOtherCount);
  }

  /** How many the Show more button adds. */
  get nextConditionPageCount(): number {
    return Math.min(CONDITION_PAGE_SIZE, this.remainingOtherCount);
  }

  /** Conditions listed now, the charted one included. */
  get shownConditionCount(): number {
    return (this.legend.reference ? 1 : 0) + this.visibleOthers.length;
  }

  /** Every condition of the index. */
  get totalConditionCount(): number {
    return (this.legend.reference ? 1 : 0) + this.legend.others.length;
  }

  /** The pager row is offered only when the others do not fit on one page. */
  get conditionsPaged(): boolean {
    return this.legend.others.length > CONDITION_PAGE_SIZE;
  }

  /**
   * Lists the next page, and moves focus to its first item: the button that had it may just have
   * disappeared, and a keyboard reader should land on what was added.
   */
  showMoreConditions(): void {
    const firstNew = this.legend.others[this.visibleOtherCount];
    if (firstNew == null) {
      return;
    }
    this.visibleOtherCount += CONDITION_PAGE_SIZE;
    this.cdr.detectChanges();
    document.getElementById(this.conditionSummaryId(firstNew))?.focus();
  }

  toggleTechnicalDetails(): void {
    this.showTechnicalDetails = !this.showTechnicalDetails;
    this.cdr.detectChanges();
  }

  /** The DOM id of an item's `<summary>`, which Show more moves focus to. */
  conditionSummaryId(item: ConditionLegendItem): string {
    return `csp-cond-summary-${item.condition.ordinal}`;
  }

  /** The item's badge, in the same hue its rows carry in the two source tables. */
  conditionItemBadgeClass(item: ConditionLegendItem): string {
    return conditionHueClass(item.condition.ordinal);
  }

  /** "differs on 2 settings", or nothing for the charted condition. */
  differsOnLabel(item: ConditionLegendItem): string {
    return `differs on ${this.countLabel(item.differingKeyLabels.length, 'setting')}`;
  }

  /**
   * What one condition differs from the charted one on, key by key, charted value first.
   *
   * Read through the condition's representative member, which speaks for the whole cohort.
   */
  differenceSummaries(item: ConditionLegendItem): ConditionDifferenceLine[] {
    const key = item.representativeKey;
    if (key == null) {
      return [];
    }
    // Read first: a replaced index clears the cache.
    void this.legend;
    let lines = this.differenceCache.get(item.condition.ordinal);
    if (lines == null) {
      const detail = conditionDetailFor(this.comparabilityIndex, key, this.sourceRunIds(key));
      lines = (detail?.rows ?? []).map(row => ({
        name: row.name,
        label: row.label,
        summary: summarizeConditionDifference(row)
      }));
      this.differenceCache.set(item.condition.ordinal, lines);
    }
    return lines;
  }

  /** "Run 12, Run 15, Analysis group 3 and 4 more": the sources in one condition. */
  memberSummary(item: ConditionLegendItem): string {
    const labels = item.memberKeys
      .slice(0, MEMBER_SUMMARY_LABELS)
      .map(key => this.legend.entryByKey.get(key))
      .filter((entry): entry is BenchmarkComparabilityIndexEntryDto => entry != null)
      .map(entry => sourceLabel(entry));
    if (labels.length === 0) {
      return 'None recorded.';
    }
    const rest = item.memberKeys.length - MEMBER_SUMMARY_LABELS;
    return rest > 0 ? `${labels.join(', ')} and ${rest} more` : labels.join(', ');
  }

  /** Opens the comparability detail of the condition's representative source, over this dialog. */
  openConditionDetailFor(item: ConditionLegendItem, event: Event): void {
    if (item.representativeKey != null) {
      this.openConditionDetail(item.representativeKey, event);
    }
  }

  /** Every condition label offered by the index, in whatever order they arrive plus any extras. */
  get conditionFilterOptions(): string[] {
    const options = new Set<string>();
    for (const condition of this.comparabilityIndex?.conditions ?? []) {
      options.add(condition.label);
    }
    for (const entry of this.comparabilityIndex?.entries ?? []) {
      if (entry.conditionOrdinal === 0) {
        options.add(entry.conditionLabel);
      }
    }
    return Array.from(options).sort((a, b) => a.localeCompare(b));
  }

  /** The largest condition's label, or the selection's own once it touches exactly one condition. */
  private compatibleConditionLabel(): string {
    const conditions = this.comparabilityIndex?.conditions ?? [];
    if (conditions.length === 0) {
      return '';
    }
    const ordinals = selectedConditions(this.comparabilityIndex, this.selectedRunIds, this.selectedGroupIds);
    const ordinal = ordinals[0] ?? 1;
    return conditions.find(condition => condition.ordinal === ordinal)?.label ?? conditions[0].label;
  }

  get showCompatibleRunsOnly(): boolean {
    const label = this.compatibleConditionLabel();
    return label !== '' && this.runTable.filters['condition'] === label;
  }

  get showCompatibleGroupsOnly(): boolean {
    const label = this.compatibleConditionLabel();
    return label !== '' && this.groupTable.filters['condition'] === label;
  }

  toggleShowCompatibleRunsOnly(): void {
    const label = this.compatibleConditionLabel();
    if (label === '') {
      return;
    }
    this.runTable.setFilter('condition', this.showCompatibleRunsOnly ? '' : label);
    this.cdr.detectChanges();
  }

  toggleShowCompatibleGroupsOnly(): void {
    const label = this.compatibleConditionLabel();
    if (label === '') {
      return;
    }
    this.groupTable.setFilter('condition', this.showCompatibleGroupsOnly ? '' : label);
    this.cdr.detectChanges();
  }

  // ---------------------------------------------------------------------------------------------
  // Rendering helpers
  // ---------------------------------------------------------------------------------------------

  /**
   * Only a completed run carries the scored answers a point is computed from.
   *
   * A run that is still executing, was cancelled or failed stays in the table, disabled and with
   * the reason as its accessible name: an operator who started a benchmark should be able to see
   * why it cannot be charted rather than hunt for a row that was silently filtered away.
   */
  isRunSelectable(run: BenchmarkRunSummaryDto): boolean {
    const status = this.runStatus(run);
    return status === 'Completed'
      || status === 'Completed with limits'
      || status === 'Completed with errors';
  }

  runUnselectableReason(run: BenchmarkRunSummaryDto): string {
    return `Run ${run.id} is ${this.runStatus(run).toLowerCase()} — only a completed run can be compared`;
  }

  /**
   * The run's status as a label, whether the server serialised the enum by name or by number.
   *
   * The numeric arm mirrors `BenchmarkRunStatus` in `GnollHackServer.Data/BenchmarkRun.cs`, which
   * starts at 1; the same mapping is spelled out in `AdminBenchmarkComponent.formatStatusLabel`,
   * and the two must agree or the Run History and the picker would name one run differently.
   */
  runStatus(run: BenchmarkRunSummaryDto): string {
    const status = run.status;
    if (status === 1 || status === 'Running') return 'Running';
    if (status === 2 || status === 'Completed') return 'Completed';
    if (status === 3 || status === 'CompletedWithErrors') return 'Completed with errors';
    if (status === 4 || status === 'Failed') return 'Failed';
    if (status === 5 || status === 'Canceled') return 'Canceled';
    if (status === 6 || status === 'CompletedWithLimits') return 'Completed with limits';
    return String(status);
  }

  /** The statuses actually present in the offered runs, so a retired one drops out on its own. */
  get runStatusOptions(): string[] {
    const seen = new Set<string>();
    for (const run of this.runs) {
      seen.add(this.runStatus(run));
    }
    return Array.from(seen).sort();
  }

  /** The tested model with its thinking level and reasoning mode, so `max` or `pro` filters too. */
  runModelText(run: BenchmarkRunSummaryDto): string {
    return [run.testedModelDisplayNameUsed, run.testedModelThinkingLevelUsed, run.testedModelReasoningModeUsed]
      .filter(part => !!part?.trim())
      .join(' ');
  }

  /** The group's tested model, or null for an empty group, which has no member run to read it from. */
  groupModelName(group: BenchmarkRunGroupDto): string | null {
    return group.testedModelDisplayName?.trim() || group.testedModelId?.trim() || null;
  }

  /** The group's tested model with its thinking level and reasoning mode, as the run table has it. */
  groupModelText(group: BenchmarkRunGroupDto): string {
    return [this.groupModelName(group), group.testedModelThinkingLevel, group.testedModelReasoningMode]
      .filter(part => !!part?.trim())
      .join(' ');
  }

  /**
   * The group's name under its model, or null when the name only repeats the row's own columns.
   * With no model to lead with, the name is the row's only line whatever it says.
   */
  groupCaption(group: BenchmarkRunGroupDto): string | null {
    const modelName = this.groupModelName(group);
    if (modelName != null && isGeneratedGroupName(group.name, group.suiteName, modelName)) {
      return null;
    }
    return group.name.trim() || null;
  }

  /** `analysis group 12, GPT-5.6 Luna`, plus the name where the row shows it: the row's accessible subject. */
  groupSubject(group: BenchmarkRunGroupDto): string {
    const parts = [`analysis group ${group.id}`];
    const modelName = this.groupModelName(group);
    if (modelName != null) {
      parts.push(modelName);
    }
    const caption = this.groupCaption(group);
    if (caption != null) {
      parts.push(`"${caption}"`);
    }
    return parts.join(', ');
  }

  analysisState(group: BenchmarkRunGroupDto): string {
    if (group.latestAnalysisId == null) {
      return 'Not analyzed';
    }
    return group.analysisStale ? 'Stale' : 'Current';
  }

  tierClass(tier: string | null | undefined): string {
    switch (tier) {
      case 'Replicate': return 'tier-replicate';
      case 'QualityComparable': return 'tier-quality';
      case 'CrossCondition': return 'tier-cross';
      default: return 'tier-none';
    }
  }

  tierName(tier: string | null | undefined): string {
    switch (tier) {
      case 'Replicate': return 'Tier A — Replicate';
      case 'QualityComparable': return 'Tier B — Quality-comparable';
      case 'CrossCondition': return 'Tier C — Cross-condition';
      default: return 'Not comparable';
    }
  }

  formatDate(value: string | null | undefined): string {
    if (!value) {
      return '—';
    }
    const parsed = new Date(value);
    return isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
  }

  formatIndex(value: number | null | undefined): string {
    return value == null || !Number.isFinite(value) ? '—' : value.toFixed(1);
  }

  onTableChanged(): void {
    this.cdr.detectChanges();
  }

  private emitSelection(runIds: number[], groupIds: number[]): void {
    this.selectionChange.emit({ runIds, groupIds });
  }
}
