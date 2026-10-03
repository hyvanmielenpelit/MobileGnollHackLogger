import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  inject
} from '@angular/core';

import { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryLeaderboardClassDto,
  BenchmarkBatteryLeaderboardDto,
  BenchmarkBatteryLeaderboardRowDto
} from '../../../services/admin-benchmark.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TableState } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { MAX_COMPARISON_SOURCES } from '../model-comparison/comparison-source-picker.component';
import { RunFactBadge, runFactBadges } from '../run-report-frame/run-facts';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import {
  batteryRunStatusLabel,
  formatCost,
  formatIndexWithHalfWidth,
  formatInterval,
  formatNumber,
  httpErrorText
} from './battery.models';

/** What the host knows of the battery whose leaderboard it opens; only the hash and the name are required. */
export interface BatteryLeaderboardOpenRequest {
  readonly definitionSha256: string;
  readonly name: string;
  readonly revision?: number | null;
  readonly schemeLabel?: string | null;
  readonly suiteCount?: number | null;
}

/** The value range one class's interval strips share. */
export interface IntervalStripScale {
  readonly min: number;
  readonly max: number;
}

/** Where a row's interval and point estimate fall on its class's strip, as percentages of the track. */
export interface IntervalStripGeometry {
  readonly startPct: number;
  readonly endPct: number;
  readonly pointPct: number;
  /** The interval was clamped at 0. */
  readonly truncatedLow: boolean;
  /** The interval was clamped at 100. */
  readonly truncatedHigh: boolean;
}

const STRIP_PADDING = 0.05;

function finite(value: number | null | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

/**
 * The scale every strip of one class shares: its lowest lower bound to its highest upper bound,
 * padded by 5 % of the span on each side. A row without an interval contributes its point
 * estimate. Null when no row has a value.
 */
export function intervalStripScale(rows: readonly BenchmarkBatteryLeaderboardRowDto[]): IntervalStripScale | null {
  let low = Number.POSITIVE_INFINITY;
  let high = Number.NEGATIVE_INFINITY;
  for (const row of rows) {
    const lower = finite(row.overallIndexLower) ? row.overallIndexLower : row.overallIndex;
    const upper = finite(row.overallIndexUpper) ? row.overallIndexUpper : row.overallIndex;
    if (finite(lower)) low = Math.min(low, lower);
    if (finite(upper)) high = Math.max(high, upper);
  }
  if (!Number.isFinite(low) || !Number.isFinite(high)) {
    return null;
  }
  const span = high - low;
  const pad = span > 0 ? span * STRIP_PADDING : 1;
  return { min: low - pad, max: high + pad };
}

/** A row's strip on `scale`; null without a point estimate or a scale. */
export function intervalStripGeometry(
  row: BenchmarkBatteryLeaderboardRowDto,
  scale: IntervalStripScale | null
): IntervalStripGeometry | null {
  if (!scale || !finite(row.overallIndex)) {
    return null;
  }
  const span = scale.max - scale.min;
  const pct = (value: number) => Math.min(100, Math.max(0, ((value - scale.min) / span) * 100));
  const point = row.overallIndex;
  const lower = finite(row.overallIndexLower) ? row.overallIndexLower : point;
  const upper = finite(row.overallIndexUpper) ? row.overallIndexUpper : point;
  const half = row.overallIndexHalfWidth;
  return {
    startPct: pct(lower),
    endPct: pct(upper),
    pointPct: pct(point),
    truncatedLow: finite(half) && point - half < 0,
    truncatedHigh: finite(half) && point + half > 100
  };
}

/** Whether two rows' 95 % intervals share at least one value; false when either has none. */
export function intervalsOverlap(a: BenchmarkBatteryLeaderboardRowDto, b: BenchmarkBatteryLeaderboardRowDto): boolean {
  if (!finite(a.overallIndexLower) || !finite(a.overallIndexUpper)
    || !finite(b.overallIndexLower) || !finite(b.overallIndexUpper)) {
    return false;
  }
  return a.overallIndexLower <= b.overallIndexUpper && b.overallIndexLower <= a.overallIndexUpper;
}

/** When a row's analysis was computed, in milliseconds; 0 when it does not parse. */
function computedAtMs(row: BenchmarkBatteryLeaderboardRowDto): number {
  if (!row.computedAtUtc) return 0;
  const ms = parseServerUtcDate(row.computedAtUtc).getTime();
  return Number.isNaN(ms) ? 0 : ms;
}

/** Newest first: the analysis time, then the battery run id. */
function newerFirst(a: BenchmarkBatteryLeaderboardRowDto, b: BenchmarkBatteryLeaderboardRowDto): number {
  return computedAtMs(b) - computedAtMs(a) || b.batteryRunId - a.batteryRunId;
}

/**
 * The battery run ids Model Comparison receives for one class's ranked rows: all of them while
 * they fit in `max`, else the oldest `max` (the newest are dropped first), in rank order.
 */
export function comparisonSourceIds(
  rankedRows: readonly BenchmarkBatteryLeaderboardRowDto[],
  max: number = MAX_COMPARISON_SOURCES
): number[] {
  if (rankedRows.length <= max) {
    return rankedRows.map(r => r.batteryRunId);
  }
  const dropped = new Set([...rankedRows].sort(newerFirst).slice(0, rankedRows.length - max).map(r => r.batteryRunId));
  return rankedRows.filter(r => !dropped.has(r.batteryRunId)).map(r => r.batteryRunId);
}

/** One ranked row as the table renders it. */
export interface LeaderboardRowView {
  readonly row: BenchmarkBatteryLeaderboardRowDto;
  /** The index rank: the row's position in server order, 1-based, whatever the table's sort. */
  readonly rank: number;
  /** Its interval overlaps the interval of the row ranked directly above it. */
  readonly overlapsAbove: boolean;
  readonly badges: readonly RunFactBadge[];
  readonly strip: IntervalStripGeometry | null;
  readonly analyzedIso: string | null;
  readonly analyzedText: string;
}

/** One comparability class as a tab and a table. */
export interface LeaderboardClassView {
  readonly cls: BenchmarkBatteryLeaderboardClassDto;
  /** `Class A`, `Class B`…, in tab order. */
  readonly name: string;
  readonly rows: readonly LeaderboardRowView[];
}

function analyzedAt(value: string | null | undefined): { iso: string | null; text: string } {
  if (!value) return { iso: null, text: '—' };
  const date = parseServerUtcDate(value);
  return Number.isNaN(date.getTime()) ? { iso: null, text: '—' } : { iso: date.toISOString(), text: date.toLocaleString() };
}

function classLetter(index: number): string {
  let n = index;
  let letters = '';
  do {
    letters = String.fromCharCode(65 + (n % 26)) + letters;
    n = Math.floor(n / 26) - 1;
  } while (n >= 0);
  return letters;
}

/** The classes in tab order: most rows first, then the class with the newest analysis. */
export function buildLeaderboardClassViews(board: BenchmarkBatteryLeaderboardDto | null): LeaderboardClassView[] {
  const classes = [...(board?.classes ?? [])];
  const newest = new Map(classes.map((c): [BenchmarkBatteryLeaderboardClassDto, number] =>
    [c, Math.max(0, ...c.rows.map(computedAtMs))]));
  classes.sort((a, b) => b.rows.length - a.rows.length || (newest.get(b) ?? 0) - (newest.get(a) ?? 0));
  return classes.map((cls, index) => {
    const scale = intervalStripScale(cls.rows);
    return {
      cls,
      name: `Class ${classLetter(index)}`,
      rows: cls.rows.map((row, i) => {
        const analyzed = analyzedAt(row.computedAtUtc);
        return {
          row,
          rank: i + 1,
          overlapsAbove: i > 0 && intervalsOverlap(row, cls.rows[i - 1]),
          badges: runFactBadges({
            name: row.testedModelLabel || row.testedModelId || 'not recorded',
            provider: row.testedProvider ?? null,
            thinkingLevel: row.testedThinkingLevel ?? null,
            reasoningMode: row.testedReasoningMode ?? null,
            serviceTier: row.testedServiceTier ?? null,
            customEndpoint: false
          }),
          strip: intervalStripGeometry(row, scale),
          analyzedIso: analyzed.iso,
          analyzedText: analyzed.text
        };
      })
    };
  });
}

/**
 * The leaderboard of one battery definition, full screen: one ranked table per comparability
 * class (a tab each when there are several), the unranked incomplete battery runs, and shortcuts to
 * Model Comparison and to each battery run's report.
 *
 * `open()` shows it and loads the leaderboard; Escape, the header's Close and *Open in Model
 * Comparison* close it and emit `closed`. The eye button asks the shell, through
 * `BenchmarkShellBridge`, to open the Battery Run Report over this dialog. The browser returns focus
 * to the eye button when that modal report closes; `restoreFocusAfterReport()` does the same for a
 * host that closes the report another way.
 */
@Component({
  selector: 'app-battery-leaderboard-dialog',
  standalone: true,
  imports: [InfoTipComponent, ProviderBadgeComponent, SortHeaderComponent],
  templateUrl: './battery-leaderboard-dialog.component.html',
  styleUrls: ['./battery-leaderboard-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatteryLeaderboardDialogComponent implements OnInit, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private bridge = inject(BenchmarkShellBridge);
  private cdr = inject(ChangeDetectorRef);

  /** Escape, the header's Close, and Open in Model Comparison. */
  @Output() closed = new EventEmitter<void>();

  @ViewChild('leaderboardDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('leaderboardTitle') title?: ElementRef<HTMLElement>;

  readonly maxComparisonSources = MAX_COMPARISON_SOURCES;
  readonly formatCost = formatCost;
  readonly formatIndexWithHalfWidth = formatIndexWithHalfWidth;
  readonly formatInterval = formatInterval;
  readonly formatNumber = formatNumber;
  readonly statusLabel = batteryRunStatusLabel;

  request: BatteryLeaderboardOpenRequest | null = null;
  leaderboard: BenchmarkBatteryLeaderboardDto | null = null;
  classViews: LeaderboardClassView[] = [];
  loading = false;
  loadError: string | null = null;
  /** The shown class, by hash, so a refresh keeps it. */
  selectedClassSha: string | null = null;

  readonly table = new TableState<LeaderboardRowView>('overallIndex', 'desc').registerAccessors({
    model: r => r.row.testedModelLabel || r.row.testedModelId || null,
    overallIndex: r => r.row.overallIndex ?? null,
    speed: r => r.row.overallSpeedIndex ?? null,
    passCost: r => r.row.passCost ?? null,
    analyzed: r => (r.analyzedIso ? new Date(r.analyzedIso) : null)
  });

  private isOpen = false;
  private destroyed = false;
  private loadSubscription: Subscription | null = null;
  /** The battery run whose eye button opened the report, focused again when the report closes. */
  private reportOpenerRunId: number | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.loadSubscription?.unsubscribe();
  }

  // --- Dialog lifecycle ------------------------------------------------------------------------

  /** Shows the dialog on `request`'s definition and loads its leaderboard. */
  open(request: BatteryLeaderboardOpenRequest): void {
    const sameDefinition = this.request?.definitionSha256 === request.definitionSha256;
    this.request = request;
    if (!sameDefinition) {
      this.leaderboard = null;
      this.classViews = [];
      this.selectedClassSha = null;
      this.table.setSort('overallIndex', 'desc');
    }
    this.reportOpenerRunId = null;
    this.isOpen = true;
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.load();
    this.cdr.detectChanges();
    this.title?.nativeElement.focus();
  }

  close(): void {
    this.loadSubscription?.unsubscribe();
    this.loadSubscription = null;
    this.loading = false;
    const wasOpen = this.isOpen;
    this.isOpen = false;
    this.reportOpenerRunId = null;
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
    if (wasOpen) {
      this.closed.emit();
    }
    this.cdr.markForCheck();
  }

  /** Escape: closes this dialog and nothing behind it. */
  onCancel(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.close();
  }

  /** The native close, however it came: stopped here, and the state follows when it was not `close()`. */
  onDialogClose(event: Event): void {
    event.stopPropagation();
    if (this.isOpen) {
      this.close();
    }
  }

  /**
   * Focuses the eye button that opened the Battery Run Report, or the title when that button is
   * gone (a refresh, another class), for a host that hears the report close.
   */
  restoreFocusAfterReport(): void {
    if (!this.isOpen) return;
    const id = this.reportOpenerRunId;
    const opener = id != null ? this.dialog?.nativeElement.querySelector<HTMLElement>(`#bl-view-${id}`) : null;
    if (opener?.isConnected) {
      opener.focus();
    } else {
      this.title?.nativeElement.focus();
    }
  }

  // --- Loading ---------------------------------------------------------------------------------

  /** Reloads the leaderboard, keeping the shown class while it still exists. */
  refresh(): void {
    if (!this.request || this.loading) return;
    this.load();
    this.cdr.markForCheck();
  }

  /** Retry after a failure; focus goes to the title, since the Retry button leaves with the alert. */
  retry(): void {
    this.refresh();
    this.cdr.detectChanges();
    this.title?.nativeElement.focus();
  }

  private load(): void {
    const hash = this.request?.definitionSha256;
    if (!hash) return;
    this.loadSubscription?.unsubscribe();
    this.loading = true;
    this.loadError = null;
    this.loadSubscription = this.benchmarkService.getBatteryLeaderboard(hash).subscribe({
      next: (board) => {
        if (this.request?.definitionSha256 !== hash) return;
        this.loading = false;
        this.applyLeaderboard(board);
        this.render();
      },
      error: (err) => {
        if (this.request?.definitionSha256 !== hash) return;
        this.loading = false;
        this.loadError = httpErrorText(err, 'Could not load the leaderboard.');
        this.render();
      }
    });
  }

  private applyLeaderboard(board: BenchmarkBatteryLeaderboardDto): void {
    this.leaderboard = board;
    this.classViews = buildLeaderboardClassViews(board);
    if (!this.classViews.some(v => v.cls.comparabilityClassSha256 === this.selectedClassSha)) {
      this.selectedClassSha = this.classViews[0]?.cls.comparabilityClassSha256 ?? null;
    }
  }

  private render(): void {
    if (this.destroyed) return;
    this.cdr.detectChanges();
    refreshAnchorPositioning();
  }

  // --- Header ----------------------------------------------------------------------------------

  get displayName(): string {
    return this.leaderboard?.batteryName || this.request?.name || 'Battery';
  }

  private get allRows(): BenchmarkBatteryLeaderboardRowDto[] {
    const board = this.leaderboard;
    return board ? [...board.classes.flatMap(c => c.rows), ...board.incomplete] : [];
  }

  /** The request's revision, else the newest revision among the rows. */
  get revision(): number | null {
    if (this.request?.revision != null) return this.request.revision;
    const revisions = this.allRows.map(r => r.definitionRevision).filter(finite);
    return revisions.length > 0 ? Math.max(...revisions) : null;
  }

  get suiteCount(): number | null {
    if (this.request?.suiteCount != null) return this.request.suiteCount;
    return this.allRows.find(r => r.suiteCount > 0)?.suiteCount ?? null;
  }

  /** Runs per suite when every battery run of the definition used the same number; else null. */
  get uniformRunsPerSuite(): number | null {
    const values = new Set(this.allRows.map(r => r.runsPerSuite));
    return values.size === 1 ? [...values][0] : null;
  }

  get shortHash(): string {
    return (this.request?.definitionSha256 ?? '').slice(0, 8);
  }

  // --- Classes ---------------------------------------------------------------------------------

  get selectedIndex(): number {
    const index = this.classViews.findIndex(v => v.cls.comparabilityClassSha256 === this.selectedClassSha);
    return index >= 0 ? index : 0;
  }

  get selectedClass(): LeaderboardClassView | null {
    return this.classViews[this.selectedIndex] ?? null;
  }

  selectClass(index: number): void {
    const view = this.classViews[index];
    if (!view) return;
    this.selectedClassSha = view.cls.comparabilityClassSha256;
    this.cdr.markForCheck();
  }

  /** §5 keyboard model: Left / Right move and wrap, Home / End jump; focus follows selection. */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const count = this.classViews.length;
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: count - 1
    };
    const requested = targets[event.key];
    if (requested === undefined || count === 0) {
      return;
    }
    event.preventDefault();
    const next = (requested + count) % count;
    this.selectClass(next);
    this.cdr.detectChanges();
    this.dialog?.nativeElement.querySelector<HTMLElement>(`#bl-tab-${next}`)?.focus();
  }

  /** The shown class's rows in the table's sort; Rank stays each row's index rank. */
  get sortedRows(): LeaderboardRowView[] {
    return this.table.viewAll(this.selectedClass?.rows ?? []);
  }

  onTableChanged(): void {
    this.cdr.markForCheck();
  }

  // --- Actions ---------------------------------------------------------------------------------

  /** Model Comparison needs two ranked results of the shown class. */
  get canOpenComparison(): boolean {
    return (this.selectedClass?.rows.length ?? 0) >= 2;
  }

  /** How many of the shown class's newest results Model Comparison leaves out; 0 when all fit. */
  get comparisonDroppedCount(): number {
    return Math.max(0, (this.selectedClass?.rows.length ?? 0) - MAX_COMPARISON_SOURCES);
  }

  /** Opens the comparison wizard with the shown class's ranked results selected, and closes this dialog. */
  openInModelComparison(): void {
    const view = this.selectedClass;
    if (!view || !this.canOpenComparison) return;
    const batteryRunIds = comparisonSourceIds(view.rows.map(r => r.row));
    this.close();
    this.bridge.openComparisonWizard({ batteryRunIds });
  }

  /** Asks the shell to open the Battery Run Report over this dialog. */
  viewReport(batteryRunId: number): void {
    this.reportOpenerRunId = batteryRunId;
    this.bridge.openBatteryRunReport(batteryRunId);
  }
}
