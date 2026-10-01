import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAnalysisDto,
  BenchmarkBatteryDto,
  BenchmarkBatteryLeaderboardDto,
  BenchmarkBatteryLeaderboardRowDto,
  BenchmarkBatteryRunDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import { exactFilter, TableState } from '../../../shared/data-table/table-state';
import { BatteryEditorDialogComponent } from './battery-editor-dialog.component';
import {
  BenchmarkBatteryComparison,
  BenchmarkBatteryStatisticsResult,
  batteryRunStatusLabel,
  batterySchemeLabel,
  formatCost,
  formatIndexWithHalfWidth,
  formatInterval,
  formatMs,
  formatNumber,
  formatPercent,
  formatPValue,
  formatSigned,
  httpErrorText,
  randomizationMethodLabel,
  reproducibilitySourceLabel
} from './battery.models';

/** What two leaderboard rows chosen for Compare would be, judged from the rows alone. */
export type BatteryCompareKind = 'model' | 'verification' | 'unlikely' | 'same' | 'none';

/**
 * The Multi-Suite sub-tab: battery definitions, battery runs, the analysis of one battery run, the
 * leaderboard of its definition and a paired comparison of two of its results.
 *
 * It loads its own data. The host hears of a battery run to show (`openBatteryRun`, which opens the
 * progress dialog) and of every change to the battery list (`batteriesChanged`, for the launcher).
 */
@Component({
  selector: 'app-benchmark-batteries',
  standalone: true,
  imports: [InfoTipComponent, SortHeaderComponent, TablePagerComponent, BatteryEditorDialogComponent],
  templateUrl: './batteries.component.html',
  styleUrls: ['./batteries.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkBatteriesComponent implements OnInit {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  /** A battery run id whose progress dialog the host opens. */
  @Output() openBatteryRun = new EventEmitter<number>();
  /** After a battery is created, edited, archived, restored or deleted. */
  @Output() batteriesChanged = new EventEmitter<void>();

  @ViewChild(BatteryEditorDialogComponent) editor?: BatteryEditorDialogComponent;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('analysisHeading') analysisHeading?: ElementRef<HTMLElement>;

  readonly formatCost = formatCost;
  readonly formatIndexWithHalfWidth = formatIndexWithHalfWidth;
  readonly formatInterval = formatInterval;
  readonly formatMs = formatMs;
  readonly formatNumber = formatNumber;
  readonly formatPercent = formatPercent;
  readonly formatPValue = formatPValue;
  readonly formatSigned = formatSigned;
  readonly schemeLabel = batterySchemeLabel;
  readonly statusLabel = batteryRunStatusLabel;
  readonly reproducibilitySourceLabel = reproducibilitySourceLabel;
  readonly randomizationMethodLabel = randomizationMethodLabel;

  // --- Batteries -------------------------------------------------------------------------------
  batteries: BenchmarkBatteryDto[] = [];
  batteriesLoading = false;
  batteriesError: string | null = null;
  batteryActionError: string | null = null;
  showArchived = false;
  suites: BenchmarkSuiteDto[] = [];
  private suitesLoaded = false;

  pendingDelete: BenchmarkBatteryDto | null = null;
  deleting = false;
  deleteError: string | null = null;

  // --- Battery runs ----------------------------------------------------------------------------
  batteryRuns: BenchmarkBatteryRunDto[] = [];
  runsLoading = false;
  runsError: string | null = null;
  readonly runTable = new TableState<BenchmarkBatteryRunDto>('startedAtUtc', 'desc').registerAccessors(
    {
      battery: r => r.batteryName,
      model: r => r.testedModelLabel ?? null,
      status: r => r.status,
      suites: r => (r.suiteCount > 0 ? r.completedSuiteCount / r.suiteCount : null),
      overallIndex: r => r.overallIndex ?? null,
      overallSpeed: r => r.overallSpeedIndex ?? null,
      totalCost: r => r.totalCost ?? null,
      startedAtUtc: r => parseServerUtcDate(r.startedAtUtc)
    },
    {
      battery: exactFilter(r => r.batteryName),
      status: exactFilter(r => r.status)
    }
  );

  // --- Analysis --------------------------------------------------------------------------------
  selectedBatteryRunId: number | null = null;
  /** The selected battery run when it is not in the loaded list. */
  private selectedRunFallback: BenchmarkBatteryRunDto | null = null;
  analysis: BenchmarkBatteryAnalysisDto | null = null;
  analysisLoading = false;
  analysisError: string | null = null;
  recomputing = false;

  // --- Leaderboard and Compare -----------------------------------------------------------------
  leaderboardHash: string | null = null;
  leaderboard: BenchmarkBatteryLeaderboardDto | null = null;
  leaderboardLoading = false;
  leaderboardError: string | null = null;

  compareBaselineId: number | null = null;
  compareTreatmentId: number | null = null;
  comparing = false;
  comparison: BenchmarkBatteryAnalysisDto | null = null;
  compareRefusal: string | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.refresh();
  }

  /** Reloads every list, and the open analysis and leaderboard. */
  refresh(): void {
    this.loadBatteries();
    this.loadSuites(true);
    this.loadRuns();
    if (this.selectedBatteryRunId != null) {
      this.loadAnalysis(this.selectedBatteryRunId);
    }
    if (this.leaderboardHash) {
      this.loadLeaderboard(this.leaderboardHash);
    }
  }

  /** Selects a battery run, loads its analysis and its definition's leaderboard, and moves focus to the analysis. */
  showAnalysis(batteryRunId: number): void {
    this.selectedBatteryRunId = batteryRunId;
    this.selectedRunFallback = null;
    this.analysis = null;
    this.analysisError = null;
    this.loadAnalysis(batteryRunId);

    const run = this.batteryRuns.find(r => r.id === batteryRunId);
    if (run) {
      this.selectLeaderboard(run.definitionSha256);
    } else {
      this.benchmarkService.getBatteryRun(batteryRunId).subscribe({
        next: (detail) => {
          if (this.selectedBatteryRunId !== batteryRunId) return;
          this.selectedRunFallback = detail;
          this.selectLeaderboard(detail.definitionSha256);
          this.cdr.markForCheck();
        },
        error: (err) => {
          this.analysisError = httpErrorText(err, 'Could not load the battery run.');
          this.cdr.markForCheck();
        }
      });
    }

    this.cdr.detectChanges();
    const heading = this.analysisHeading?.nativeElement;
    if (heading) {
      heading.focus({ preventScroll: true });
      const reduce = typeof window !== 'undefined'
        && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
      heading.scrollIntoView?.({ block: 'start', behavior: reduce ? 'auto' : 'smooth' });
    }
  }

  // =============================================================================================
  // Batteries
  // =============================================================================================

  get visibleBatteries(): BenchmarkBatteryDto[] {
    return this.showArchived ? this.batteries : this.batteries.filter(b => !b.isArchived);
  }

  get archivedCount(): number {
    return this.batteries.filter(b => b.isArchived).length;
  }

  toggleShowArchived(): void {
    this.showArchived = !this.showArchived;
    this.cdr.markForCheck();
  }

  private loadBatteries(): void {
    this.batteriesLoading = true;
    this.batteriesError = null;
    this.benchmarkService.getBatteries().subscribe({
      next: (batteries) => {
        this.batteries = batteries;
        this.batteriesLoading = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.batteriesLoading = false;
        this.batteriesError = httpErrorText(err, 'Could not load the batteries.');
        this.cdr.markForCheck();
      }
    });
  }

  private loadSuites(force = false): void {
    if (this.suitesLoaded && !force) return;
    this.benchmarkService.getSuites().subscribe({
      next: (suites) => {
        this.suites = suites;
        this.suitesLoaded = true;
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.batteryActionError = httpErrorText(err, 'Could not load the suites for the battery editor.');
        this.cdr.markForCheck();
      }
    });
  }

  /** The declared weight preview of a battery, in suite order; empty when the weights are undefined. */
  declaredWeights(battery: BenchmarkBatteryDto): number[] {
    return battery.weightPreviews?.find(p => p.declared)?.weights ?? [];
  }

  newBattery(): void {
    this.batteryActionError = null;
    this.editor?.open(null, this.suites);
  }

  editBattery(battery: BenchmarkBatteryDto): void {
    this.batteryActionError = null;
    this.editor?.open(battery, this.suites);
  }

  onBatterySaved(battery: BenchmarkBatteryDto): void {
    const index = this.batteries.findIndex(b => b.id === battery.id);
    this.batteries = index >= 0
      ? this.batteries.map(b => (b.id === battery.id ? battery : b))
      : [...this.batteries, battery].sort((a, b) => a.name.localeCompare(b.name));
    this.batteriesChanged.emit();
    this.loadBatteries();
    this.cdr.markForCheck();
  }

  toggleArchive(battery: BenchmarkBatteryDto): void {
    this.batteryActionError = null;
    this.benchmarkService.archiveBattery(battery.id, !battery.isArchived).subscribe({
      next: (updated) => {
        this.batteries = this.batteries.map(b => (b.id === updated.id ? updated : b));
        this.batteriesChanged.emit();
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.batteryActionError = httpErrorText(err, `Could not ${battery.isArchived ? 'restore' : 'archive'} ${battery.name}.`);
        this.cdr.markForCheck();
      }
    });
  }

  showLeaderboardFor(battery: BenchmarkBatteryDto): void {
    this.selectLeaderboard(battery.definitionSha256);
  }

  requestDelete(battery: BenchmarkBatteryDto): void {
    if (battery.hasActiveBatteryRun) {
      return;
    }
    this.pendingDelete = battery;
    this.deleteError = null;
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  closeDeleteDialog(): void {
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
    this.pendingDelete = null;
    this.deleteError = null;
    this.cdr.markForCheck();
  }

  onDeleteCancel(event: Event): void {
    event.preventDefault();
    if (!this.deleting) {
      this.closeDeleteDialog();
    }
  }

  confirmDelete(): void {
    const battery = this.pendingDelete;
    if (!battery || this.deleting) return;
    this.deleting = true;
    this.deleteError = null;
    this.benchmarkService.deleteBattery(battery.id).subscribe({
      next: () => {
        this.deleting = false;
        this.batteries = this.batteries.filter(b => b.id !== battery.id);
        this.closeDeleteDialog();
        this.batteriesChanged.emit();
        this.loadRuns();
      },
      error: (err) => {
        this.deleting = false;
        this.deleteError = httpErrorText(err, `Could not delete ${battery.name}.`);
        this.cdr.markForCheck();
      }
    });
  }

  // =============================================================================================
  // Battery runs
  // =============================================================================================

  private loadRuns(): void {
    this.runsLoading = true;
    this.runsError = null;
    this.benchmarkService.getBatteryRuns().subscribe({
      next: (runs) => {
        this.batteryRuns = runs;
        this.runsLoading = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.runsLoading = false;
        this.runsError = httpErrorText(err, 'Could not load the battery runs.');
        this.cdr.markForCheck();
      }
    });
  }

  /** The battery names present in the loaded runs, for the Battery filter. */
  get runBatteryNames(): string[] {
    return [...new Set(this.batteryRuns.map(r => r.batteryName))]
      .sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));
  }

  get runStatuses(): string[] {
    return [...new Set(this.batteryRuns.map(r => r.status))].sort();
  }

  onRunFilter(column: 'battery' | 'status', event: Event): void {
    this.runTable.setFilter(column, (event.target as HTMLSelectElement).value);
    this.cdr.markForCheck();
  }

  clearRunFilters(): void {
    this.runTable.clearFilters();
    this.cdr.markForCheck();
  }

  onTableChanged(): void {
    this.cdr.markForCheck();
  }

  showProgress(run: BenchmarkBatteryRunDto): void {
    this.openBatteryRun.emit(run.id);
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    const date = parseServerUtcDate(value);
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
  }

  isoDate(value: string | null | undefined): string | null {
    if (!value) return null;
    const date = parseServerUtcDate(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  // =============================================================================================
  // Analysis
  // =============================================================================================

  get selectedRun(): BenchmarkBatteryRunDto | null {
    if (this.selectedBatteryRunId == null) return null;
    return this.batteryRuns.find(r => r.id === this.selectedBatteryRunId) ?? this.selectedRunFallback;
  }

  get result(): BenchmarkBatteryStatisticsResult | null {
    return this.analysis?.result ?? null;
  }

  /** Recompute is called out when the stored analysis may no longer describe the battery run. */
  get recomputeCalledOut(): boolean {
    const run = this.selectedRun;
    return !!this.analysis && (
      this.analysis.stale
      || (this.analysis.excludedMembers?.length ?? 0) > 0
      || !!run?.analysisStale
      || !!run?.analysisHasExcludedMembers);
  }

  private loadAnalysis(batteryRunId: number): void {
    this.analysisLoading = true;
    this.analysisError = null;
    this.benchmarkService.getBatteryAnalysis(batteryRunId).subscribe({
      next: (analysis) => {
        if (this.selectedBatteryRunId !== batteryRunId) return;
        this.analysis = analysis;
        this.analysisLoading = false;
        if (analysis && !this.leaderboardHash) {
          this.selectLeaderboard(analysis.definitionSha256);
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (this.selectedBatteryRunId !== batteryRunId) return;
        this.analysisLoading = false;
        this.analysisError = httpErrorText(err, 'Could not load the analysis.');
        this.cdr.markForCheck();
      }
    });
  }

  recompute(): void {
    const id = this.selectedBatteryRunId;
    if (id == null || this.recomputing) return;
    this.recomputing = true;
    this.analysisError = null;
    this.benchmarkService.analyseBatteryRun(id).subscribe({
      next: (analysis) => {
        this.recomputing = false;
        if (this.selectedBatteryRunId === id) {
          this.analysis = analysis;
        }
        this.loadRuns();
        if (this.leaderboardHash) {
          this.loadLeaderboard(this.leaderboardHash);
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.recomputing = false;
        this.analysisError = httpErrorText(err, 'The analysis could not be computed.');
        this.cdr.markForCheck();
      }
    });
  }

  downloadReport(): void {
    const id = this.selectedBatteryRunId;
    if (id == null || !this.analysis) return;
    window.open(this.benchmarkService.getBatteryReportUrl(id), '_blank');
  }

  suiteName(index: number): string {
    const fromResult = this.result?.suites?.find(s => s.suiteIndex === index)?.suiteName;
    const fromRun = this.selectedRun?.suites?.find(s => s.index === index)?.suiteName;
    return fromResult ?? fromRun ?? `Suite ${index + 1}`;
  }

  suiteNames(indexes: readonly number[] | null | undefined): string {
    return (indexes ?? []).map(i => this.suiteName(i)).join(', ');
  }

  /** The declared scheme's index, for the sensitivity differences. */
  get declaredSchemeIndex(): number | null {
    return this.result?.weightingSensitivity?.find(s => s.declared)?.index ?? null;
  }

  roleCosts(costs: Record<string, number> | null | undefined): { role: string; cost: number }[] {
    return Object.entries(costs ?? {}).map(([role, cost]) => ({ role, cost }));
  }

  // =============================================================================================
  // Leaderboard
  // =============================================================================================

  private selectLeaderboard(hash: string | null | undefined): void {
    if (!hash) return;
    if (hash !== this.leaderboardHash) {
      this.leaderboardHash = hash;
      this.leaderboard = null;
      this.compareBaselineId = null;
      this.compareTreatmentId = null;
      this.comparison = null;
      this.compareRefusal = null;
    }
    this.loadLeaderboard(hash);
  }

  private loadLeaderboard(hash: string): void {
    this.leaderboardLoading = true;
    this.leaderboardError = null;
    this.benchmarkService.getBatteryLeaderboard(hash).subscribe({
      next: (board) => {
        if (this.leaderboardHash !== hash) return;
        this.leaderboard = board;
        this.leaderboardLoading = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (this.leaderboardHash !== hash) return;
        this.leaderboardLoading = false;
        this.leaderboardError = httpErrorText(err, 'Could not load the leaderboard.');
        this.cdr.markForCheck();
      }
    });
  }

  get leaderboardTitle(): string {
    const board = this.leaderboard;
    if (board?.batteryName) return board.batteryName;
    const run = this.batteryRuns.find(r => r.definitionSha256 === this.leaderboardHash);
    return run?.batteryName ?? 'Battery';
  }

  shortHash(hash: string | null | undefined): string {
    return hash ? hash.slice(0, 8) : '—';
  }

  // =============================================================================================
  // Compare
  // =============================================================================================

  /** Every ranked leaderboard row with the class it belongs to, for the two Compare selects. */
  get compareCandidates(): { classLabel: string; classSha: string; row: BenchmarkBatteryLeaderboardRowDto }[] {
    return (this.leaderboard?.classes ?? []).flatMap(c =>
      c.rows.map(row => ({ classLabel: c.label, classSha: c.comparabilityClassSha256, row })));
  }

  compareOptionLabel(row: BenchmarkBatteryLeaderboardRowDto): string {
    return `#${row.batteryRunId} · ${row.testedModelLabel ?? 'unknown model'} · ${formatNumber(row.overallIndex)}`;
  }

  onCompareSelect(side: 'baseline' | 'treatment', event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    const id = value === '' ? null : Number(value);
    if (side === 'baseline') {
      this.compareBaselineId = id;
    } else {
      this.compareTreatmentId = id;
    }
    this.comparison = null;
    this.compareRefusal = null;
    this.cdr.markForCheck();
  }

  get compareKind(): BatteryCompareKind {
    const candidates = this.compareCandidates;
    const baseline = candidates.find(c => c.row.batteryRunId === this.compareBaselineId);
    const treatment = candidates.find(c => c.row.batteryRunId === this.compareTreatmentId);
    if (!baseline || !treatment) return 'none';
    if (baseline.row.batteryRunId === treatment.row.batteryRunId) return 'same';
    if (baseline.classSha === treatment.classSha) return 'model';
    const sameModel = baseline.row.testedModelConfigurationId != null
      && baseline.row.testedModelConfigurationId === treatment.row.testedModelConfigurationId;
    return sameModel ? 'verification' : 'unlikely';
  }

  get canCompare(): boolean {
    const kind = this.compareKind;
    return !this.comparing && kind !== 'none' && kind !== 'same';
  }

  compare(): void {
    if (!this.canCompare || this.compareBaselineId == null || this.compareTreatmentId == null) return;
    const baselineId = this.compareBaselineId;
    const treatmentId = this.compareTreatmentId;
    this.comparing = true;
    this.comparison = null;
    this.compareRefusal = null;
    this.benchmarkService.analyseBatteryRun(treatmentId, baselineId).subscribe({
      next: (analysis) => {
        this.comparing = false;
        this.comparison = analysis;
        if (this.selectedBatteryRunId === treatmentId) {
          this.analysis = analysis;
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.comparing = false;
        this.compareRefusal = httpErrorText(err, 'The two battery results could not be compared.');
        this.cdr.markForCheck();
      }
    });
  }

  get comparisonResult(): BenchmarkBatteryComparison | null {
    return this.comparison?.comparison ?? null;
  }
}
