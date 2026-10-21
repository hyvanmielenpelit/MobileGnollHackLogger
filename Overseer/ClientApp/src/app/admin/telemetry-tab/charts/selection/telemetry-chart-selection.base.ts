import {
  AfterViewChecked,
  ChangeDetectorRef,
  Directive,
  ElementRef,
  NgZone,
  OnChanges,
  OnDestroy,
  OnInit,
  SimpleChanges,
  computed,
  inject,
  input,
  output,
  signal
} from '@angular/core';
import { ensureOverlayPolyfills } from '../../../../utils/polyfills.util';
import { isImeComposing } from '../../../../utils/ime.util';
import { ModelPickerMatchPart, matchParts, searchTerms } from '../../../../shared/model-picker/model-picker-view';
import { formatInteger } from '../../../benchmark/shared/provider-availability/provider-availability';
import type { TelemetrySort, TelemetrySortFieldInfo } from '../../sort-popover/telemetry-sort-popover.component';
import {
  ChartListNoun,
  ChartRowFilter,
  ChartSelectionRow,
  TL_CHARTS_SEARCH_DEBOUNCE_MS,
  TL_CHARTS_SELECTION_BATCH,
  absentKeys,
  capWarningText,
  chooseShown,
  coversEveryRow,
  effectiveFilter,
  filterChartRows,
  selectionBandText,
  selectionStatusText,
  sortChartRows
} from './telemetry-chart-filters';

/** What sets one chooser apart: its ids, its noun, its sort fields and view, and its absent rows. */
export interface ChartSelectionConfig<R extends ChartSelectionRow, F extends string> {
  idPrefix: string;
  noun: ChartListNoun;
  sortFields: readonly TelemetrySortFieldInfo<F>[];
  defaultSort: TelemetrySort<F>;
  readSort: () => TelemetrySort<F>;
  writeSort: (sort: TelemetrySort<F>) => void;
  absentRow: (key: string, name?: string) => R;
}

/**
 * The charts dialog's Models, Users and Groups choosers, shared: the selection band with its chips,
 * the search, the sort, *Selected only*, the status line, *Select all* and *Select all shown*, the cap
 * and the batches. Presentational: the dialog owns the rows and the chosen keys, and each change of
 * the choice is emitted as the new keys in choice order.
 */
@Directive()
export abstract class TelemetryChartSelectionBase<R extends ChartSelectionRow, F extends string>
  implements OnInit, OnChanges, AfterViewChecked, OnDestroy {
  protected readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  protected readonly cdr = inject(ChangeDetectorRef);
  private readonly zone = inject(NgZone);

  /** The options of the scope, in the server's order. */
  readonly rows = input<readonly R[]>([]);
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  /** The chosen keys, in choice order. */
  readonly selectedKeys = input<readonly string[]>([]);
  /** The most keys a partial choice may hold. */
  readonly max = input(50);

  /** The new chosen keys, in choice order. */
  readonly selectionChange = output<string[]>();
  /** Retry was pressed after a load error. */
  readonly retry = output<void>();

  /** What sets the list apart; each list provides its own. */
  protected abstract readonly config: ChartSelectionConfig<R, F>;

  readonly batch = TL_CHARTS_SELECTION_BATCH;
  readonly formatInteger = formatInteger;

  /** The search field as typed. */
  searchInput = '';
  /** The search the list applies; follows `searchInput` after a short quiet. Never stored. */
  readonly search = signal('');
  /** The sort chosen in this view; null until one is, when the stored sort applies. */
  private readonly chosenSort = signal<TelemetrySort<F> | null>(null);
  /** The sort, remembered per browser. */
  readonly sort = computed<TelemetrySort<F>>(() => this.chosenSort() ?? this.config.readSort());
  /** *Selected only*, as pressed; it applies only while something is chosen. Never stored. */
  readonly selectedOnlyPressed = signal(false);
  /** How many matching rows the list renders. */
  readonly visibleCount = signal(TL_CHARTS_SELECTION_BATCH);

  /** Whether the chip row wraps in a box of its own (Show all N). */
  chipsExpanded = false;
  /** Whether the collapsed chip row overflows its one line. */
  chipsOverflow = false;

  /** The name each key last had in the rows, so a chosen key that leaves the scope keeps it. */
  private readonly knownNames = new Map<string, string>();
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private chipsObserver: ResizeObserver | null = null;
  private observedChips: HTMLElement | null = null;
  private measuredKeys: readonly string[] | null = null;
  private chipsMeasureQueued = false;

  /** Unique in the document; every id and anchor name of the list derives from it. */
  get idPrefix(): string {
    return this.config.idPrefix;
  }

  get noun(): ChartListNoun {
    return this.config.noun;
  }

  get sortFields(): readonly TelemetrySortFieldInfo<F>[] {
    return this.config.sortFields;
  }

  get defaultSort(): TelemetrySort<F> {
    return this.config.defaultSort;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['rows'] && !changes['rows'].firstChange) {
      this.visibleCount.set(this.batch);
    }
  }

  ngAfterViewChecked(): void {
    this.watchChips();
  }

  ngOnDestroy(): void {
    this.cancelSearchTimer();
    this.chipsObserver?.disconnect();
    this.chipsObserver = null;
  }

  // --- Hooks for the lists ---

  /** Whether the list holds only part of the options (the Users list's cap). */
  protected truncatedList(): boolean {
    return false;
  }

  /** The options before any cap; the rows' count unless the list is capped. */
  protected totalCount(): number {
    return this.rows().length;
  }

  /** The list's own filters (the Models list's provider and key-source toggles). */
  protected extraFilter(): Partial<ChartRowFilter> {
    return {};
  }

  /** Whether the list's own filters narrow it. */
  protected extraNarrowing(): boolean {
    return false;
  }

  /** Clears the list's own filters. */
  protected clearExtraFilters(): void {
    // No filters of its own.
  }

  /** Stores the view; the Models list also stores its providers. */
  protected saveView(): void {
    this.config.writeSort(this.sort());
  }

  // --- Derived ---

  /** Whether the list holds only part of the options. */
  readonly isTruncated = computed(() => this.truncatedList());

  readonly chosenSet = computed(() => new Set(this.selectedKeys()));

  /** Each row by key; also remembers each row's name for when its key leaves the scope. */
  readonly rowByKey = computed(() => {
    const byKey = new Map<string, R>();
    for (const row of this.rows()) {
      byKey.set(row.key, row);
      this.knownNames.set(row.key, row.name);
    }
    return byKey;
  });

  /** One chip per chosen key, in choice order. */
  readonly chipRows = computed(() => {
    const byKey = this.rowByKey();
    return this.selectedKeys().map(key => byKey.get(key) ?? this.config.absentRow(key, this.knownNames.get(key)));
  });

  /** The chosen keys the rows do not hold, as rows. */
  readonly absentRows = computed(() => {
    this.rowByKey();
    return absentKeys(this.selectedKeys(), this.rows()).map(key => this.config.absentRow(key, this.knownNames.get(key)));
  });

  readonly selectedOnly = computed(() => this.selectedOnlyPressed() && this.selectedKeys().length > 0);

  readonly filter = computed<ChartRowFilter>(() => ({
    search: this.search(),
    selectedOnly: this.selectedOnly(),
    chosen: this.selectedKeys(),
    ...this.extraFilter()
  }));

  readonly sortedRows = computed(() => sortChartRows(this.rows(), this.sort()));

  /** The rows the search and the filters let through, sorted. */
  readonly matching = computed(() => filterChartRows(this.sortedRows(), this.filter()));

  /** The absent chosen rows the search lets through; listed first. */
  readonly matchingAbsent = computed(() => filterChartRows(this.absentRows(), this.filter()));

  readonly visibleRows = computed(() => this.matching().slice(0, this.visibleCount()));

  /** What the list renders: the absent chosen rows, then a batch of the matching rows. */
  readonly listItems = computed<readonly R[]>(() => [...this.matchingAbsent(), ...this.visibleRows()]);

  readonly searchTerms = computed(() => searchTerms(this.search()));

  /** A search or a toggle hides rows. *Selected only* is not counted: it hides only unchosen rows. */
  readonly narrowed = computed(() => this.searchTerms().length > 0 || this.extraNarrowing());

  /** Every row is chosen in an untruncated list: the same as no filter. */
  readonly coversAll = computed(() => !this.isTruncated() && coversEveryRow(this.selectedKeys(), this.rows()));

  /** What the dialog sends for this choice. */
  readonly effective = computed(() => effectiveFilter(this.selectedKeys(), this.rows(), this.isTruncated()));

  /** A partial choice at the cap. */
  readonly atCap = computed(() => this.effective().length > 0 && this.selectedKeys().length >= this.max());

  /** The rows not chosen. */
  private readonly unchosenCount = computed(() => {
    const chosen = this.chosenSet();
    return this.rows().filter(row => !chosen.has(row.key)).length;
  });

  readonly remaining = computed(() => Math.max(0, this.matching().length - this.visibleCount()));

  readonly nextBatch = computed(() => Math.min(this.batch, this.remaining()));

  // --- Texts ---

  get bandText(): string {
    return selectionBandText(this.noun, this.selectedKeys().length, this.totalCount(), this.coversAll());
  }

  /** Empty while loading (the loading indicator says so), after an error and for an empty list. */
  get statusText(): string {
    if (this.loading() || this.error() || this.rows().length === 0) return '';
    return selectionStatusText(this.noun, this.visibleRows().length, this.rows().length, this.selectedKeys().length,
      this.isTruncated() ? { truncated: true, total: this.totalCount() } : undefined);
  }

  get capText(): string {
    return capWarningText(this.noun, this.max(), !this.isTruncated());
  }

  get clearFiltersShown(): boolean {
    return this.searchInput.trim() !== '' || this.search().trim() !== '' || this.extraNarrowing();
  }

  nameParts(row: R): ModelPickerMatchPart[] {
    return matchParts(row.name, this.searchTerms());
  }

  isChosen(key: string): boolean {
    return this.chosenSet().has(key);
  }

  /**
   * An unchosen row that the cap refuses: choosing it would leave a partial choice over the cap.
   * Choosing the last unchosen row of an untruncated list is the same as no filter, so it is allowed.
   */
  isRefused(row: R): boolean {
    if (this.isChosen(row.key) || this.selectedKeys().length + 1 <= this.max()) {
      return false;
    }
    return this.isTruncated() || this.unchosenCount() !== 1;
  }

  rowId(index: number): string {
    return `${this.idPrefix}-opt-${index}`;
  }

  /** The row's meta id, then the cap line's while the cap refuses it. */
  describedBy(row: R, index: number): string {
    const meta = `${this.rowId(index)}-meta`;
    return this.isRefused(row) ? `${meta} ${this.idPrefix}-cap` : meta;
  }

  /** Rows from the 51st on render lazily. */
  isDeferred(index: number): boolean {
    return index - this.matchingAbsent().length >= this.batch;
  }

  // --- Select all ---

  /** *Select all*: while nothing narrows the list, it is not capped, and some row is not chosen. */
  get selectAllShown(): boolean {
    return !this.narrowed() && !this.isTruncated() && this.rows().length > 0 && !this.coversAll();
  }

  /** *Select all shown (N)*: while the list is narrowed and shows rows. */
  get selectShownShown(): boolean {
    return this.narrowed() && this.matching().length > 0;
  }

  /** Why Select all shown cannot add, or null when it can. */
  get selectShownRefusal(): string | null {
    const shown = this.matching();
    if (shown.every(row => this.isChosen(row.key))) {
      return `Every ${this.noun.one} shown is already chosen.`;
    }
    const next = chooseShown(this.selectedKeys(), shown, this.rows(), this.isTruncated(), this.max());
    return next.length > this.selectedKeys().length ? null : `At most ${formatInteger(this.max())} ${this.noun.many} can be chosen.`;
  }

  /** Every row: the same as no filter, never capped. Focus goes to the band's label, as the button goes. */
  selectAll(): void {
    if (!this.selectAllShown) return;
    const chosen = this.selectedKeys();
    const set = new Set(chosen);
    this.emitSelection([...chosen, ...this.sortedRows().map(r => r.key).filter(key => !set.has(key))]);
    this.focusLabelAfterRender();
  }

  /** The shown rows, up to the cap; refused while it cannot add (the button is aria-disabled). */
  selectShown(): void {
    if (this.selectShownRefusal !== null) return;
    this.emitSelection(chooseShown(this.selectedKeys(), this.matching(), this.rows(), this.isTruncated(), this.max()));
  }

  // --- A row ---

  /** Chooses or unchooses the row; a click the cap refuses is prevented, and the checkbox keeps its focus. */
  onRowClick(row: R, event: Event): void {
    const chosen = this.selectedKeys();
    if (this.isChosen(row.key)) {
      this.emitSelection(chosen.filter(key => key !== row.key));
      return;
    }
    if (this.isRefused(row)) {
      event.preventDefault();
      return;
    }
    this.emitSelection([...chosen, row.key]);
  }

  // --- The band ---

  /** Focuses the band's label. */
  focusLabel(): void {
    this.host.nativeElement.querySelector<HTMLElement>(`#${this.idPrefix}-label`)?.focus();
  }

  /** Clear selection; the button goes with the choice, so focus moves to the band's label. */
  clearSelection(): void {
    this.chipsExpanded = false;
    this.chipsOverflow = false;
    this.emitSelection([]);
    this.focusLabel();
  }

  /**
   * One chip's remove button. Focus moves to the next chip's remove button, else to Clear selection
   * while other chips remain, else to the band's label; chips are tracked by key, so those survive.
   */
  removeChip(key: string, index: number): void {
    const buttons = Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('.tl-sel-chip-remove'));
    const remaining = this.selectedKeys().filter(k => k !== key);
    const target = index + 1 < buttons.length
      ? buttons[index + 1]
      : remaining.length > 0 ? this.host.nativeElement.querySelector<HTMLElement>(`#${this.idPrefix}-clear`) : null;
    if (remaining.length === 0) {
      this.chipsExpanded = false;
    }
    this.emitSelection(remaining);
    if (target?.isConnected) {
      target.focus();
    } else {
      this.focusLabel();
    }
  }

  /** Show all N / Show fewer: the chips wrap in their own box, or go back to one line. */
  toggleChipsExpanded(): void {
    this.chipsExpanded = !this.chipsExpanded;
    this.cdr.detectChanges();
    this.measureChips();
  }

  /** A chip's remove button scrolls itself into view in the one-line row when it takes focus. */
  revealChip(event: FocusEvent): void {
    (event.target as HTMLElement | null)?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
  }

  // --- Search, sort and filters ---

  onSearchInput(event: Event): void {
    this.searchInput = (event.target as HTMLInputElement).value;
    this.cancelSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      this.applySearch(this.searchInput);
      this.cdr.markForCheck();
    }, TL_CHARTS_SEARCH_DEBOUNCE_MS);
  }

  /** Escape with text clears the search at once and stays in the dialog; with none, Escape is left alone. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (isImeComposing(event)) return;
    if (event.key !== 'Escape' || this.searchInput === '') return;
    event.preventDefault();
    event.stopPropagation();
    this.clearSearch();
  }

  onSortChange(sort: TelemetrySort): void {
    const info = this.sortFields.find(f => f.field === sort.field);
    if (!info) return;
    this.chosenSort.set({ field: info.field, direction: sort.direction });
    this.visibleCount.set(this.batch);
    this.saveView();
  }

  toggleSelectedOnly(): void {
    this.selectedOnlyPressed.set(!this.selectedOnly());
    this.visibleCount.set(this.batch);
  }

  /** Clears the search and the toggles, keeps *Selected only*, and focuses the search field. */
  clearFilters(): void {
    this.clearSearch();
    this.clearExtraFilters();
    this.visibleCount.set(this.batch);
    this.cdr.detectChanges();
    this.host.nativeElement.querySelector<HTMLElement>(`#${this.idPrefix}-search`)?.focus();
  }

  /** Renders the next batch, or every row, and focuses the first new row's checkbox. */
  showMore(all = false): void {
    const first = this.matchingAbsent().length + this.visibleCount();
    this.visibleCount.set(all ? this.matching().length : this.visibleCount() + this.batch);
    this.cdr.detectChanges();
    this.host.nativeElement.querySelector<HTMLElement>(`#${this.rowId(first)}`)?.focus();
  }

  onRetry(): void {
    this.retry.emit();
  }

  protected resetBatch(): void {
    this.visibleCount.set(this.batch);
  }

  private clearSearch(): void {
    this.cancelSearchTimer();
    this.searchInput = '';
    this.applySearch('');
  }

  private applySearch(text: string): void {
    if (text.trim() !== this.search().trim()) {
      this.visibleCount.set(this.batch);
    }
    this.search.set(text);
  }

  private cancelSearchTimer(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  private emitSelection(keys: string[]): void {
    this.selectionChange.emit(keys);
    this.cdr.markForCheck();
  }

  private focusLabelAfterRender(): void {
    this.cdr.detectChanges();
    this.focusLabel();
  }

  // --- Chip overflow ---

  /**
   * Keeps the chip row's overflow state current. The row exists only while something is chosen, so
   * the observer follows it in and out of the DOM; a new chip grows the row's scroll width without
   * resizing the row, so a changed choice is measured again as well.
   */
  private watchChips(): void {
    const chips = this.host.nativeElement.querySelector<HTMLElement>('.tl-sel-chips');
    if (chips !== this.observedChips) {
      this.chipsObserver?.disconnect();
      this.chipsObserver = null;
      this.observedChips = chips;
      if (chips !== null && typeof ResizeObserver !== 'undefined') {
        this.chipsObserver = new ResizeObserver(() => this.zone.run(() => this.measureChips()));
        this.chipsObserver.observe(chips);
      }
    }
    if (chips !== null && this.measuredKeys !== this.selectedKeys() && !this.chipsMeasureQueued) {
      // Outside the check pass: a bound field changed inside this hook faults the check.
      this.chipsMeasureQueued = true;
      queueMicrotask(() => {
        this.chipsMeasureQueued = false;
        this.measureChips();
      });
    }
  }

  /** Whether the collapsed chip row overflows its one line. Kept as it was while expanded. */
  private measureChips(): void {
    const chips = this.observedChips;
    this.measuredKeys = this.selectedKeys();
    if (chips === null || !chips.isConnected || this.chipsExpanded) {
      return;
    }
    const overflow = chips.scrollWidth > chips.clientWidth + 1;
    if (overflow !== this.chipsOverflow) {
      this.chipsOverflow = overflow;
      this.cdr.markForCheck();
    }
  }
}
