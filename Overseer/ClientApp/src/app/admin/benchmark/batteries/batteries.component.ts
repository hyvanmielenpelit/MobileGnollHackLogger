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
import { NgTemplateOutlet } from '@angular/common';

import {
  AdminBenchmarkService,
  BenchmarkBatteryDto,
  BenchmarkBatterySuiteDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { CardListChip, CardListFacet, CardListState } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { BatteryEditorDialogComponent } from './battery-editor-dialog.component';
import { BatteryLeaderboardDialogComponent } from './battery-leaderboard-dialog.component';
import {
  BATTERY_CARD_SUITE_ROWS,
  BATTERY_FILTER_BAR_MIN_EXCLUSIVE,
  BATTERY_LIST_SORTS,
  BATTERY_LIST_VIEW_STORAGE_KEY,
  BATTERY_SCHEME_OPTIONS,
  BatteryWeightSegment,
  DEFAULT_BATTERY_LIST_SORT,
  batterySchemeLabel,
  batteryWeightMix,
  formatPercent,
  httpErrorText
} from './battery.models';

/** A battery's weighting scheme as its card and the Weighting facet show it. */
function schemeLabelOf(battery: BenchmarkBatteryDto): string {
  return battery.weightingSchemeLabel || batterySchemeLabel(battery.weightingScheme);
}

/** The text the search field matches: the name, the description and the suite names. */
function batterySearchText(battery: BenchmarkBatteryDto): string {
  return [battery.name, battery.description ?? '', ...battery.suites.map(s => s.suiteName)].join('\n').toLowerCase();
}

/** A timestamp's milliseconds, or 0 when it does not parse. */
function timeOf(value: string | null | undefined): number {
  if (!value) return 0;
  const ms = parseServerUtcDate(value).getTime();
  return Number.isNaN(ms) ? 0 : ms;
}

/**
 * The Multi-Suite sub-tab: the battery definitions as a card list, with New Battery, Edit,
 * Archive / Restore, Delete and each definition's leaderboard.
 *
 * It loads its own data. The host hears of every change to the battery list (`batteriesChanged`,
 * for the launcher). The leaderboard dialog it hosts reaches the shell's Battery Run Report through
 * `BenchmarkShellBridge`, which the shell provides.
 */
@Component({
  selector: 'app-benchmark-batteries',
  standalone: true,
  imports: [NgTemplateOutlet, InfoTipComponent, FilterFacetComponent, BatteryEditorDialogComponent, BatteryLeaderboardDialogComponent],
  templateUrl: './batteries.component.html',
  styleUrls: ['./batteries.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkBatteriesComponent implements OnInit, OnDestroy {
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** After a battery is created, edited, archived, restored or deleted. */
  @Output() batteriesChanged = new EventEmitter<void>();

  @ViewChild(BatteryEditorDialogComponent) editor?: BatteryEditorDialogComponent;
  @ViewChild('leaderboardDialog') leaderboardDialog?: BatteryLeaderboardDialogComponent;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;

  readonly formatPercent = formatPercent;
  readonly schemeLabelOf = schemeLabelOf;
  readonly sorts = BATTERY_LIST_SORTS;
  readonly suiteRows = BATTERY_CARD_SUITE_ROWS;

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

  /** The cards whose suite table shows every row. */
  private readonly expandedSuites = new Set<number>();
  /** The battery whose Leaderboard button opened the dialog, focused again when it closes. */
  private leaderboardOpenerId: number | null = null;

  readonly table = new TableState<BenchmarkBatteryDto>('modified', 'desc').registerAccessors(
    {
      modified: b => timeOf(b.modifiedAtUtc),
      name: b => b.name,
      runs: b => b.batteryRunCount,
      suites: b => b.suites.length
    },
    {
      search: customFilter((b, value) => batterySearchText(b).includes(value.trim().toLowerCase())),
      suite: anyOfFilter(b => b.suites.map(s => s.suiteName)),
      weighting: anyOfFilter(b => schemeLabelOf(b))
    }
  );

  /** The card list over `table`: the search, Sort by, the facets, the chips and the batch. */
  readonly list = new CardListState<BenchmarkBatteryDto>(this.table, {
    idPrefix: 'bb',
    sorts: BATTERY_LIST_SORTS,
    defaultSort: DEFAULT_BATTERY_LIST_SORT,
    storageKey: BATTERY_LIST_VIEW_STORAGE_KEY,
    facets: [
      { column: 'suite', label: 'Suite', values: b => b.suites.map(s => s.suiteName) },
      { column: 'weighting', label: 'Weighting', values: b => schemeLabelOf(b), order: BATTERY_SCHEME_OPTIONS.map(o => o.label) }
    ],
    onChange: () => this.cdr.markForCheck()
  });

  /** `visibleBatteries`, kept as one array until the batteries or Show archived change, for the facet memo. */
  private visibleMemo: { batteries: BenchmarkBatteryDto[]; showArchived: boolean; rows: BenchmarkBatteryDto[] } | null = null;
  /** Each battery's weight bar, built once per battery object. */
  private readonly weightMixCache = new WeakMap<BenchmarkBatteryDto, BatteryWeightSegment[]>();

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.refresh();
  }

  ngOnDestroy(): void {
    this.list.dispose();
  }

  /** Reloads the batteries and the suites the editor offers. */
  refresh(): void {
    this.loadBatteries();
    this.loadSuites(true);
  }

  // --- The list --------------------------------------------------------------------------------

  /** The batteries the list holds: every one while Show archived is pressed, else the unarchived. */
  get visibleBatteries(): BenchmarkBatteryDto[] {
    const memo = this.visibleMemo;
    if (memo && memo.batteries === this.batteries && memo.showArchived === this.showArchived) {
      return memo.rows;
    }
    const rows = this.showArchived ? this.batteries : this.batteries.filter(b => !b.isArchived);
    this.visibleMemo = { batteries: this.batteries, showArchived: this.showArchived, rows };
    return rows;
  }

  get archivedCount(): number {
    return this.batteries.filter(b => b.isArchived).length;
  }

  /** The filter bar is shown above three batteries, and while a filter is still active. */
  get showFilterBar(): boolean {
    return this.visibleBatteries.length > BATTERY_FILTER_BAR_MIN_EXCLUSIVE || this.table.hasActiveFilters;
  }

  get cards(): BenchmarkBatteryDto[] {
    return this.list.view(this.visibleBatteries);
  }

  get facets(): CardListFacet[] {
    return this.list.facets(this.visibleBatteries);
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.visibleBatteries);
  }

  get listStatus(): string {
    return this.list.statusText(this.visibleBatteries, { one: 'battery', many: 'batteries' });
  }

  get noMatches(): boolean {
    return this.table.noMatches(this.visibleBatteries);
  }

  get remainingCount(): number {
    return this.list.remainingCount(this.visibleBatteries);
  }

  get nextBatchCount(): number {
    return this.list.nextBatchCount(this.visibleBatteries);
  }

  get matchingCount(): number {
    return this.list.matching(this.visibleBatteries).length;
  }

  toggleShowArchived(): void {
    this.showArchived = !this.showArchived;
    this.list.invalidate();
    this.cdr.markForCheck();
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

  /** Removes a chip's filter, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.visibleBatteries);
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.bb-filter-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#bb-search'))?.focus();
  }

  /** Clears the search and every filter, then focuses the search when it is still shown. */
  clearFilters(): void {
    this.list.clearFilters();
    this.cdr.detectChanges();
    this.element('#bb-search')?.focus();
  }

  showMore(): void {
    this.focusCard(this.list.showMore(this.visibleBatteries));
  }

  showAll(): void {
    this.focusCard(this.list.showAll(this.visibleBatteries));
  }

  /** Renders, then focuses the title of the card at `index` in the view, if there is one. */
  private focusCard(index: number): void {
    this.cdr.detectChanges();
    const battery = this.cards[index];
    if (battery) {
      this.element('#bb-card-title-' + battery.id)?.focus();
    }
  }

  /** After a delete: the card now at the deleted one's index, else the previous one, else the heading. */
  private focusAfterDelete(index: number): void {
    this.cdr.detectChanges();
    const cards = this.cards;
    const battery = index >= 0 ? cards[index] ?? cards[index - 1] : undefined;
    const target = battery ? this.element('#bb-card-title-' + battery.id) : null;
    (target ?? this.element('#bb-batteries-title'))?.focus();
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }

  // --- A card ----------------------------------------------------------------------------------

  /** The declared weight preview of a battery, in suite order; empty when the weights are undefined. */
  declaredWeights(battery: BenchmarkBatteryDto): number[] {
    return battery.weightPreviews?.find(p => p.declared)?.weights ?? [];
  }

  /** A suite's declared weight, or null when the weights are undefined. */
  suiteWeight(battery: BenchmarkBatteryDto, position: number): number | null {
    const weights = this.declaredWeights(battery);
    return weights.length === battery.suites.length ? weights[position] ?? null : null;
  }

  weightMix(battery: BenchmarkBatteryDto): BatteryWeightSegment[] {
    let segments = this.weightMixCache.get(battery);
    if (!segments) {
      segments = batteryWeightMix(battery.suites, this.declaredWeights(battery));
      this.weightMixCache.set(battery, segments);
    }
    return segments;
  }

  /** The questions of every suite, summed. */
  questionTotal(battery: BenchmarkBatteryDto): number {
    return battery.suites.reduce((sum, suite) => sum + (suite.deleted ? 0 : suite.questionCount), 0);
  }

  /** A validation error or a deleted suite keeps the battery from running. */
  isBroken(battery: BenchmarkBatteryDto): boolean {
    return battery.validationErrors.length > 0 || battery.brokenSuiteNames.length > 0 || battery.suites.some(s => s.deleted);
  }

  /** A suite still holds a question with no assessed difficulty. */
  difficultiesIncomplete(battery: BenchmarkBatteryDto): boolean {
    return battery.suites.some(s => !s.deleted && !s.difficultyFullyAssessed);
  }

  /** The first rows of the suite table, always shown. */
  leadingSuites(battery: BenchmarkBatteryDto): BenchmarkBatterySuiteDto[] {
    return battery.suites.slice(0, BATTERY_CARD_SUITE_ROWS);
  }

  /** The rows behind *Show all K suites*. */
  moreSuites(battery: BenchmarkBatteryDto): BenchmarkBatterySuiteDto[] {
    return battery.suites.slice(BATTERY_CARD_SUITE_ROWS);
  }

  suitesExpanded(battery: BenchmarkBatteryDto): boolean {
    return this.expandedSuites.has(battery.id);
  }

  /** Shows or hides the suite rows past the first four; focus stays on the toggle. */
  toggleSuites(battery: BenchmarkBatteryDto): void {
    if (this.expandedSuites.has(battery.id)) {
      this.expandedSuites.delete(battery.id);
    } else {
      this.expandedSuites.add(battery.id);
    }
    this.cdr.markForCheck();
  }

  /** `2026-10-02 14:05 UTC`, or an em dash. */
  formatModified(value: string | null | undefined): string {
    const iso = this.isoDate(value);
    return iso ? `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC` : '—';
  }

  isoDate(value: string | null | undefined): string | null {
    if (!value) return null;
    const date = parseServerUtcDate(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  shortHash(hash: string | null | undefined): string {
    return hash ? hash.slice(0, 8) : '—';
  }

  // --- Loading ---------------------------------------------------------------------------------

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

  // --- Actions ---------------------------------------------------------------------------------

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
      : [...this.batteries, battery];
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

  /** Opens the leaderboard of the battery's current definition. */
  openLeaderboard(battery: BenchmarkBatteryDto): void {
    this.leaderboardOpenerId = battery.id;
    this.leaderboardDialog?.open({
      definitionSha256: battery.definitionSha256,
      name: battery.name,
      revision: battery.revision,
      schemeLabel: schemeLabelOf(battery),
      suiteCount: battery.suites.length
    });
  }

  /** The leaderboard closed: focus returns to the Leaderboard button that opened it. */
  onLeaderboardClosed(): void {
    const id = this.leaderboardOpenerId;
    this.leaderboardOpenerId = null;
    if (id != null) {
      this.element('#bb-leaderboard-' + id)?.focus();
    }
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
        const index = this.cards.findIndex(b => b.id === battery.id);
        this.deleting = false;
        this.batteries = this.batteries.filter(b => b.id !== battery.id);
        this.expandedSuites.delete(battery.id);
        this.closeDeleteDialog();
        this.batteriesChanged.emit();
        this.focusAfterDelete(index);
      },
      error: (err) => {
        this.deleting = false;
        this.deleteError = httpErrorText(err, `Could not delete ${battery.name}.`);
        this.cdr.markForCheck();
      }
    });
  }
}
