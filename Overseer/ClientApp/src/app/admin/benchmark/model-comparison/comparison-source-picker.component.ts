import { ChangeDetectorRef, Component, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { exactFilter, TableState } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import type {
  BenchmarkRunGroupDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';

/** One selectable suite for the scope control. Structural, so any suite DTO with these two fits. */
export interface ModelComparisonSuiteOption {
  readonly id: number;
  readonly name: string;
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
  imports: [CommonModule, FormsModule, SortHeaderComponent, TablePagerComponent],
  templateUrl: './comparison-source-picker.component.html',
  styleUrls: ['./comparison-source-picker.component.scss']
})
export class ComparisonSourcePickerComponent implements OnInit {
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

  /** The figures' own plot cap, named here so the soft warning cannot drift from it. */
  @Input() maxPlotted = 8;

  @Output() suiteIdChange = new EventEmitter<number | null>();
  @Output() selectionChange = new EventEmitter<ModelComparisonSelection>();
  @Output() compare = new EventEmitter<void>();
  @Output() clear = new EventEmitter<void>();

  /** The hard cap, exposed so the template names the same number the guard enforces. */
  readonly maxSources = MAX_COMPARISON_SOURCES;

  readonly runTable = new TableState<BenchmarkRunSummaryDto>('id', 'desc').registerAccessors(
    {
      selected: r => (this.isRunSelected(r.id) ? 1 : 0),
      id: r => r.id,
      suiteName: r => r.suiteName,
      testedModel: r => r.testedModelDisplayNameUsed,
      status: r => this.runStatus(r),
      qualityIndex: r => r.qualityIndex ?? r.finalScore,
      startedAtUtc: r => new Date(r.startedAtUtc)
    },
    {
      suiteName: r => r.suiteName,
      testedModel: r => r.testedModelDisplayNameUsed,
      status: exactFilter(r => this.runStatus(r)),
      selected: exactFilter(r => (this.isRunSelected(r.id) ? 'yes' : 'no'))
    }
  );

  readonly groupTable = new TableState<BenchmarkRunGroupDto>('createdAtUtc', 'desc').registerAccessors(
    {
      selected: g => (this.isGroupSelected(g.id) ? 1 : 0),
      name: g => g.name,
      tier: g => g.tierLabel || String(g.tier),
      runCount: g => g.runCount,
      createdAtUtc: g => new Date(g.createdAtUtc),
      analysis: g => this.analysisState(g)
    },
    {
      name: g => g.name,
      tier: exactFilter(g => String(g.tier)),
      selected: exactFilter(g => (this.isGroupSelected(g.id) ? 'yes' : 'no'))
    }
  );

  ngOnInit(): void {
    // Every icon-only control below carries an interestfor tooltip, and the primitives behind those
    // are not baseline everywhere.
    ensureOverlayPolyfills();
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

  onClear(): void {
    this.clear.emit();
  }

  onCompare(): void {
    if (!this.canCompare) {
      return;
    }
    this.compare.emit();
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
  // The two caps
  // ---------------------------------------------------------------------------------------------

  /** Above the plot cap. A note, never a block: the view charts the first eight and names the rest. */
  get overPlotCap(): boolean {
    return this.selectedCount > this.maxPlotted;
  }

  /** Above the request cap. Compare refuses, with the reason named beside it. */
  get overSourceCap(): boolean {
    return this.selectedCount > this.maxSources;
  }

  get canCompare(): boolean {
    return !this.loading && this.selectedCount > 0 && !this.overSourceCap;
  }

  /** Why Compare is disabled, or the empty string while it is not. */
  get compareBlockedReason(): string {
    if (this.loading) {
      return 'A comparison is being computed.';
    }
    if (this.selectedCount === 0) {
      return 'Select at least one run or analysis group.';
    }
    if (this.overSourceCap) {
      return `${this.selectedCount} sources selected — at most ${this.maxSources} may be compared in one ` +
        'request. A comparison over every stored run is a slow query and an unreadable figure.';
    }
    return '';
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

  analysisState(group: BenchmarkRunGroupDto): string {
    if (group.latestAnalysisId == null) {
      return 'Not analysed';
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
