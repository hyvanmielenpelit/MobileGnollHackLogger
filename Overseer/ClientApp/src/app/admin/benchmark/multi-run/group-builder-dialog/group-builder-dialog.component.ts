import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Injector,
  Input,
  NgZone,
  OnDestroy,
  OnInit,
  Output,
  ViewChild,
  afterNextRender,
  inject
} from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkComparabilityResultDto,
  BenchmarkRunGroupDto,
  BenchmarkRunGroupTierPreviewDto,
  BenchmarkRunSummaryDto,
  CreateBenchmarkRunGroupRequest
} from '../../../../services/admin-benchmark.service';
import { CardListChip, CardListFacet, CardListSort, CardListState } from '../../../../shared/data-table/card-list-state';
import { FilterFacetComponent } from '../../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../../shared/data-table/table-state';
import { ModelIdentityComponent } from '../../../../shared/model-identity/model-identity.component';
import { parseServerUtcDate } from '../../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import { httpErrorText } from '../../batteries/battery.models';
import { formatStatus, formatStatusLabel } from '../../benchmark-run-format';
import { BenchmarkFingerprintEntry, FINGERPRINT_LONG_NAMES, RUN_HISTORY_LIMIT } from '../../benchmark.models';
import { runGroupTierClass, runGroupTierName } from '../../shared/run-group-format';

/** The run list's Sort by order, as `CardListState` stores it. */
export const GROUP_BUILDER_RUNS_VIEW_STORAGE_KEY = 'overseer.benchmark.multiRun.runs.view';

/** How many run cards the list shows at first, and how many more Show more adds. */
export const GROUP_BUILDER_RUNS_BATCH = 20;

export const GROUP_BUILDER_RUN_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'started', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'started', direction: 'asc' },
  { id: 'index', label: 'Highest index', column: 'index', direction: 'desc' },
  { id: 'model', label: 'Model (A–Z)', column: 'model', direction: 'asc' }
];

/** The run statuses a group analysis pools, mirroring `BenchmarkRunFinalizer.IsPoolable`. */
const SETTLED_STATUSES: readonly string[] = ['Completed', 'CompletedWithLimits', 'CompletedWithErrors'];

const TELEMETRY_MEASURED = 'Measured timing';
const TELEMETRY_ADVISORY = 'Advisory timing';

/** Whether a run has finished with a measurement, so it may join a group. */
export function isSettledRun(run: Pick<BenchmarkRunSummaryDto, 'status'>): boolean {
  return SETTLED_STATUSES.includes(formatStatus(run.status));
}

/** Why a run that has not settled cannot join a group; null for a settled run. */
export function unsettledRunReason(run: Pick<BenchmarkRunSummaryDto, 'id' | 'status'>): string | null {
  if (isSettledRun(run)) return null;
  return formatStatus(run.status) === 'Running'
    ? `Run #${run.id} is still in progress. Only a finished run can join a group.`
    : `Run #${run.id} ended ${formatStatusLabel(run.status)} and produced no measurement, so it cannot join a group.`;
}

/** The candidate as the Model facet lists it: the name, with the thinking level where one is recorded. */
export function runModelLabel(run: BenchmarkRunSummaryDto): string {
  const name = run.testedModelDisplayNameUsed || run.testedModelIdUsed || 'Model not recorded';
  return run.testedModelThinkingLevelUsed ? `${name} (${run.testedModelThinkingLevelUsed})` : name;
}

function runIndex(run: BenchmarkRunSummaryDto): number | null {
  return run.qualityIndex ?? run.finalScore ?? null;
}

function runTelemetry(run: BenchmarkRunSummaryDto): string {
  return run.speedMeasurementDegraded ? TELEMETRY_ADVISORY : TELEMETRY_MEASURED;
}

function timeOf(value: string | null | undefined): number | null {
  if (!value) return null;
  const ms = parseServerUtcDate(value).getTime();
  return Number.isNaN(ms) ? null : ms;
}

/** The text the search matches: `#id`, the model, the status and the five instrument hashes. */
function runSearchText(run: BenchmarkRunSummaryDto): string {
  return [
    `#${run.id}`,
    runModelLabel(run),
    run.testedModelIdUsed ?? '',
    formatStatusLabel(run.status),
    run.candidateSystemPromptSha256 ?? '',
    run.toolGuidesSha256 ?? '',
    run.knowledgeBaseHeadSha ?? '',
    run.wikiHeadSha ?? '',
    run.sourceCodeHeadSha ?? ''
  ].join('\n').toLowerCase();
}

/**
 * GnollBench → Multi-Run Analysis → New group: a modal that builds an analysis group by hand from
 * the suite's runs.
 *
 * The comparability tier is previewed as runs are selected, and a refusal names the keys that
 * differ and the runs carrying each value — a verdict without its reasons cannot be acted on. The
 * server names the group; a Tier C set is created only once the operator acknowledges it, and a
 * set an existing group already holds is refused with a route to that group.
 */
@Component({
  selector: 'app-group-builder-dialog',
  standalone: true,
  imports: [FilterFacetComponent, ModelIdentityComponent],
  templateUrl: './group-builder-dialog.component.html',
  styleUrls: ['./group-builder-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class GroupBuilderDialogComponent implements OnInit, AfterViewChecked, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly zone = inject(NgZone);

  /** The suite whose runs the dialog offers. */
  @Input() suiteId: number | null = null;

  /** A group the server created and, for Tier A or B, analyzed. */
  @Output() created = new EventEmitter<BenchmarkRunGroupDto>();

  /** The group that already holds exactly the selected runs, from its Open group link. */
  @Output() openGroup = new EventEmitter<number>();

  @ViewChild('dialog', { static: true }) dialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('title', { static: true }) title!: ElementRef<HTMLElement>;
  @ViewChild('selectionLabel', { static: true }) selectionLabel!: ElementRef<HTMLElement>;
  @ViewChild('noteDisclosure', { static: true }) noteDisclosure!: ElementRef<HTMLDetailsElement>;
  @ViewChild('selectionChips') private selectionChips?: ElementRef<HTMLElement>;

  readonly sorts = GROUP_BUILDER_RUN_SORTS;
  readonly tierName = runGroupTierName;
  readonly tierClass = runGroupTierClass;
  readonly modelLabel = runModelLabel;
  readonly statusLabel = formatStatusLabel;
  readonly isSettled = isSettledRun;
  readonly unsettledReason = unsettledRunReason;

  availableRuns: BenchmarkRunSummaryDto[] = [];
  loadingRuns = false;
  runsError: string | null = null;
  selectedRunIds: number[] = [];
  notes = '';

  preview: BenchmarkRunGroupTierPreviewDto | null = null;
  previewing = false;
  previewError: string | null = null;
  /** Only ever offered when the preview says Tier C; a Tier C group is refused without it. */
  crossCondition = false;
  creating = false;
  createError: string | null = null;

  /** The selection band's chips overflow their one line. */
  chipsOverflow = false;
  /** The chips wrap in a box of their own, at the operator's request. */
  chipsExpanded = false;

  /**
   * Guards against an out-of-order preview response. Checkboxes can be clicked faster than the
   * round trip returns, and a stale tier verdict rendered over a newer selection is worse than no
   * verdict at all — it names keys the operator is no longer looking at.
   */
  private previewToken = 0;
  private runsToken = 0;
  private chipsObserver: ResizeObserver | null = null;
  private observedChips: HTMLElement | null = null;
  private measuredSelection: readonly number[] | null = null;
  private chipsMeasureQueued = false;

  readonly table = new TableState<BenchmarkRunSummaryDto>('started', 'desc').registerAccessors(
    {
      started: r => timeOf(r.startedAtUtc),
      index: r => runIndex(r),
      model: r => runModelLabel(r).toLowerCase()
    },
    {
      search: customFilter((r, value) => runSearchText(r).includes(value.toLowerCase())),
      model: anyOfFilter(r => runModelLabel(r)),
      status: anyOfFilter(r => formatStatusLabel(r.status)),
      telemetry: anyOfFilter(r => runTelemetry(r)),
      selected: customFilter((r, value) => value !== 'yes' || this.isRunSelected(r.id))
    }
  );

  /** The card list over `table`: the search, Sort by, the facets, the chips and the batch. */
  readonly list = new CardListState<BenchmarkRunSummaryDto>(this.table, {
    idPrefix: 'mr-runs',
    batch: GROUP_BUILDER_RUNS_BATCH,
    sorts: GROUP_BUILDER_RUN_SORTS,
    defaultSort: 'newest',
    storageKey: GROUP_BUILDER_RUNS_VIEW_STORAGE_KEY,
    facets: [
      { column: 'model', label: 'Model', values: r => runModelLabel(r) },
      { column: 'status', label: 'Status', values: r => formatStatusLabel(r.status) },
      { column: 'telemetry', label: 'Telemetry', values: r => runTelemetry(r), order: [TELEMETRY_MEASURED, TELEMETRY_ADVISORY] }
    ],
    // Show selected only filters by the selection, so the facet counts follow it.
    memoDeps: () => [this.selectedRunIds],
    onChange: () => this.cdr.markForCheck()
  });

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngAfterViewChecked(): void {
    this.watchSelectionChips();
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.chipsObserver?.disconnect();
    this.chipsObserver = null;
    if (this.dialog?.nativeElement.open) this.dialog.nativeElement.close();
  }

  // --- Opening and closing -----------------------------------------------------------------------

  /** Starts a new group: clears the state, loads the suite's runs, opens the dialog and focuses its title. */
  open(): void {
    this.selectedRunIds = [];
    this.notes = '';
    this.preview = null;
    this.previewing = false;
    this.previewError = null;
    this.crossCondition = false;
    this.creating = false;
    this.createError = null;
    this.chipsExpanded = false;
    this.chipsOverflow = false;
    this.previewToken++;
    this.list.reset();
    this.noteDisclosure.nativeElement.open = false;
    this.loadRuns();

    const dialog = this.dialog.nativeElement;
    if (!dialog.open) dialog.showModal();
    this.cdr.detectChanges();
    this.title.nativeElement.focus();
  }

  close(): void {
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  private loadRuns(): void {
    const suiteId = this.suiteId;
    const token = ++this.runsToken;
    this.availableRuns = [];
    this.runsError = null;
    if (suiteId == null) {
      this.loadingRuns = false;
      return;
    }
    this.loadingRuns = true;
    this.benchmarkService.getRuns(suiteId, RUN_HISTORY_LIMIT).subscribe({
      next: (runs) => {
        if (token !== this.runsToken) return;
        this.availableRuns = runs ?? [];
        this.loadingRuns = false;
        this.list.invalidate();
        this.cdr.markForCheck();
        afterNextRender(() => refreshAnchorPositioning(), { injector: this.injector });
      },
      error: (err) => {
        if (token !== this.runsToken) return;
        this.loadingRuns = false;
        this.runsError = httpErrorText(err, 'Failed to load the suite runs.');
        this.cdr.markForCheck();
      }
    });
  }

  // --- Selection ---------------------------------------------------------------------------------

  isRunSelected(runId: number): boolean {
    return this.selectedRunIds.includes(runId);
  }

  get selectedRunCount(): number {
    return this.selectedRunIds.length;
  }

  /** The selected ids, ascending, for the band's chips. */
  get selectedIdsAscending(): number[] {
    return [...this.selectedRunIds].sort((a, b) => a - b);
  }

  /** `3 runs selected`, `1 run selected`, or `nothing selected yet` while the label is clipped. */
  get selectionSummary(): string {
    const count = this.selectedRunCount;
    if (count === 0) return 'nothing selected yet';
    return count === 1 ? '1 run selected' : `${count} runs selected`;
  }

  /** A run that has not settled cannot be added; one already selected can always be removed. */
  isRunBlocked(run: BenchmarkRunSummaryDto): boolean {
    return !isSettledRun(run) && !this.isRunSelected(run.id);
  }

  /** Keeps an aria-disabled checkbox from toggling: cancelling the click reverts the native check. */
  preventBlockedToggle(event: Event, run: BenchmarkRunSummaryDto): void {
    if (this.isRunBlocked(run)) {
      event.preventDefault();
    }
  }

  toggleRun(runId: number): void {
    if (!this.isRunSelected(runId)) {
      const run = this.availableRuns.find(r => r.id === runId);
      if (run && !isSettledRun(run)) return;
    }
    this.selectedRunIds = this.isRunSelected(runId)
      ? this.selectedRunIds.filter(id => id !== runId)
      : [...this.selectedRunIds, runId];
    this.createError = null;
    this.list.invalidate();

    // The tier can change on every click, and an operator selecting a fourth run wants to know
    // before pressing Create whether the fourth one broke the set.
    this.previewTier();
    this.cdr.markForCheck();
  }

  /** Clear selection: the button goes with the selection, so focus moves to the band's label. */
  clearSelection(): void {
    this.selectedRunIds = [];
    this.preview = null;
    this.previewError = null;
    this.previewing = false;
    this.previewToken++;
    this.crossCondition = false;
    this.createError = null;
    this.chipsExpanded = false;
    this.list.invalidate();
    this.cdr.detectChanges();
    this.selectionLabel.nativeElement.focus();
  }

  /**
   * A chip's remove button. Focus moves to the next chip's remove button, else to Clear selection
   * while other chips remain, else to the band's label. Chips are tracked by run id, so the other
   * chips' buttons survive the re-render.
   */
  removeFromSelection(runId: number, index: number): void {
    const buttons = this.chipRemoveButtons();
    const target = index + 1 < buttons.length
      ? buttons[index + 1]
      : this.selectedRunCount > 1 ? this.element('#mr-builder-clear-selection') : null;
    this.toggleRun(runId);
    this.cdr.detectChanges();
    (target?.isConnected ? target : this.selectionLabel.nativeElement).focus();
  }

  /** Show all N / Show fewer: the chips wrap in their own box, or go back to one line. */
  toggleChipsExpanded(): void {
    this.chipsExpanded = !this.chipsExpanded;
    this.cdr.detectChanges();
    this.measureChipsOverflow();
  }

  /** A chip's remove button scrolls itself into view in the one-line row when it takes focus. */
  revealChip(event: FocusEvent): void {
    (event.target as HTMLElement | null)?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
  }

  private chipRemoveButtons(): HTMLButtonElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.mr-sel-remove'));
  }

  /**
   * Keeps the chip row's overflow state current. The row exists only while something is selected,
   * so the observer follows it in and out of the DOM; and a new chip grows the row's scroll width
   * without resizing the row, so a changed selection is measured again as well.
   */
  private watchSelectionChips(): void {
    const chips = this.selectionChips?.nativeElement ?? null;
    if (chips !== this.observedChips) {
      this.chipsObserver?.disconnect();
      this.chipsObserver = null;
      this.observedChips = chips;
      if (chips !== null && typeof ResizeObserver !== 'undefined') {
        this.chipsObserver = new ResizeObserver(() => this.zone.run(() => this.measureChipsOverflow()));
        this.chipsObserver.observe(chips);
      }
    }
    if (chips !== null && this.measuredSelection !== this.selectedRunIds && !this.chipsMeasureQueued) {
      // Outside the check pass: a bound field changed inside this hook faults the check.
      this.chipsMeasureQueued = true;
      queueMicrotask(() => {
        this.chipsMeasureQueued = false;
        this.measureChipsOverflow();
      });
    }
  }

  /** Whether the collapsed chip row overflows its one line. Kept as it was while expanded. */
  private measureChipsOverflow(): void {
    const chips = this.selectionChips?.nativeElement ?? null;
    this.measuredSelection = this.selectedRunIds;
    if (chips === null || this.chipsExpanded) {
      if (chips === null && this.chipsOverflow) {
        this.chipsOverflow = false;
        this.cdr.markForCheck();
      }
      return;
    }
    const overflow = chips.scrollWidth > chips.clientWidth + 1;
    if (overflow !== this.chipsOverflow) {
      this.chipsOverflow = overflow;
      this.cdr.markForCheck();
    }
  }

  // --- The tier preview ----------------------------------------------------------------------------

  previewTier(): void {
    if (this.selectedRunIds.length < 2) {
      this.previewToken++;
      this.preview = null;
      this.previewing = false;
      this.previewError = null;
      this.crossCondition = false;
      return;
    }

    const token = ++this.previewToken;
    this.previewing = true;
    this.previewError = null;
    this.benchmarkService.previewRunGroupTier({
      runIds: [...this.selectedRunIds],
      crossCondition: this.crossCondition
    }).subscribe({
      next: (result) => {
        if (token !== this.previewToken) return;
        this.preview = result;
        this.previewing = false;
        // The cross-condition acknowledgment is offered only where it means something. Leaving
        // it checked after the set stops being Tier C would send a flag the server would then
        // have to ignore, and a checked box the operator cannot see is a lie about the request.
        if (!this.previewIsCrossCondition) this.crossCondition = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.previewToken) return;
        this.previewing = false;
        this.preview = null;
        this.previewError = httpErrorText(err, 'Failed to compute the comparability tier.');
        this.cdr.markForCheck();
      }
    });
  }

  onCrossConditionChange(event: Event): void {
    this.crossCondition = (event.target as HTMLInputElement).checked;
    this.createError = null;
    this.previewTier();
    this.cdr.markForCheck();
  }

  get previewComparability(): BenchmarkComparabilityResultDto | null {
    return this.preview?.comparability ?? null;
  }

  get previewIsCrossCondition(): boolean {
    return this.previewComparability?.tier === 'CrossCondition';
  }

  get previewDifferences() {
    return this.previewComparability?.differences ?? [];
  }

  /** The group that already holds exactly the selected runs, or null. */
  get duplicateOfGroupId(): number | null {
    return this.preview?.duplicateOfGroupId ?? null;
  }

  /**
   * The server's refusal, shown only where nothing else on the panel explains it: not for a
   * duplicate, which has its own warning, and not while the differing keys are listed.
   */
  get previewRefusal(): string | null {
    const preview = this.preview;
    if (!preview || preview.accepted || this.duplicateOfGroupId != null) return null;
    if (this.previewIsCrossCondition || this.previewDifferences.length > 0) return null;
    return preview.error || null;
  }

  /** `1 key matched`, `14 keys matched`. */
  matchedKeysLabel(count: number): string {
    return count === 1 ? '1 key matched' : `${count} keys matched`;
  }

  // --- Creating ------------------------------------------------------------------------------------

  /** Why Create Group is unavailable, for the footer note; null while it is available. */
  get createBlockReason(): string | null {
    if (this.selectedRunIds.length < 2) return 'Select at least two runs.';
    if (this.previewing) return 'Checking whether these runs can form a group…';
    if (this.previewError) return 'The comparability tier could not be computed. Change the selection to try again.';
    const preview = this.preview;
    if (!preview) return 'Checking whether these runs can form a group…';
    const duplicate = this.duplicateOfGroupId;
    if (duplicate != null) return `Group #${duplicate} already holds exactly these runs.`;
    const tier = this.previewComparability?.tier;
    if (tier === 'NotComparable') return 'These runs cannot form a group: the keys above differ.';
    if (tier === 'CrossCondition' && !this.crossCondition) return 'Acknowledge the cross-condition set to create it.';
    if (!preview.accepted) return preview.error || 'These runs cannot form a group.';
    return null;
  }

  get canCreateGroup(): boolean {
    return !this.creating && this.createBlockReason === null;
  }

  onNotesInput(event: Event): void {
    this.notes = (event.target as HTMLTextAreaElement).value;
  }

  /** Creates the group without a name; the server names it. A click while unavailable does nothing. */
  createGroup(): void {
    if (!this.canCreateGroup) return;

    const request: CreateBenchmarkRunGroupRequest = {
      runIds: [...this.selectedRunIds],
      crossCondition: this.crossCondition
    };
    const notes = this.notes.trim();
    if (notes) {
      request.notes = notes;
    }

    this.creating = true;
    this.createError = null;
    this.cdr.markForCheck();
    this.benchmarkService.createRunGroup(request).subscribe({
      next: (result) => {
        this.creating = false;
        if (!result?.accepted) {
          this.applyRefusal(result);
          return;
        }
        this.close();
        if (result.group) this.created.emit(result.group);
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.creating = false;
        // The server answers a refusal with 400 and the preview shape, keys and duplicate included.
        const body = err?.error as BenchmarkRunGroupTierPreviewDto | null | undefined;
        if (body && typeof body === 'object' && body.accepted === false) {
          this.applyRefusal(body);
          return;
        }
        this.createError = httpErrorText(err, 'Failed to create the group.');
        this.cdr.markForCheck();
      }
    });
  }

  /** A refusal is data, not an exception: it arrives with the differing keys or the duplicate group. */
  private applyRefusal(result: BenchmarkRunGroupTierPreviewDto | null | undefined): void {
    if (result) {
      this.preview = { ...result, comparability: result.comparability ?? this.preview?.comparability ?? null };
    }
    this.createError = result?.duplicateOfGroupId != null ? null : (result?.error || 'The group was refused.');
    this.cdr.markForCheck();
  }

  onOpenDuplicate(groupId: number): void {
    this.openGroup.emit(groupId);
  }

  // --- The run list ----------------------------------------------------------------------------------

  get cards(): BenchmarkRunSummaryDto[] {
    return this.list.view(this.availableRuns);
  }

  get facets(): CardListFacet[] {
    return this.list.facets(this.availableRuns);
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.availableRuns);
  }

  get noMatches(): boolean {
    return this.table.noMatches(this.availableRuns);
  }

  get remainingCount(): number {
    return this.list.remainingCount(this.availableRuns);
  }

  get nextBatchCount(): number {
    return this.list.nextBatchCount(this.availableRuns);
  }

  get matchingCount(): number {
    return this.list.matching(this.availableRuns).length;
  }

  /** Selected runs the list does not show: hidden by a filter or beyond the batch. */
  get hiddenSelectedCount(): number {
    const shown = new Set(this.cards.map(run => run.id));
    return this.selectedRunIds.filter(id => !shown.has(id)).length;
  }

  /** The list's one polite live region: `Showing 20 of 119 runs · filtered from 140 · 3 selected, 1 not shown`. */
  get listStatus(): string {
    if (this.loadingRuns) return 'Loading the suite runs…';
    const count = this.list.statusText(this.availableRuns, { one: 'run', many: 'runs' });
    const hidden = this.hiddenSelectedCount;
    return hidden > 0 && count ? `${count} · ${this.selectedRunCount} selected, ${hidden} not shown` : count;
  }

  get showSelectedOnly(): boolean {
    return this.table.filters['selected'] === 'yes';
  }

  /** A view setting, not a filter: no chip, and Clear all keeps it. */
  toggleShowSelectedOnly(): void {
    this.table.setFilter('selected', this.showSelectedOnly ? '' : 'yes');
    this.list.resetBatch();
    this.list.invalidate();
    this.cdr.markForCheck();
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and keeps the dialog open; in an empty field it closes the dialog. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) {
      this.cdr.markForCheck();
    }
  }

  onSortChange(event: Event): void {
    if (this.list.setSort((event.target as HTMLSelectElement).value)) {
      this.cdr.markForCheck();
    }
  }

  onFacetChange(column: string, values: string[]): void {
    this.list.setFacet(column, values);
    this.cdr.markForCheck();
  }

  /** Removes a chip's filter, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.availableRuns);
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.mr-runs-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#mr-runs-search'))?.focus();
  }

  /** Clear all: the search and every facet, keeping Show selected only; focus goes to the search. */
  clearFilters(): void {
    this.list.clearFilters(['selected']);
    this.cdr.detectChanges();
    this.element('#mr-runs-search')?.focus();
  }

  /** The nothing-matches state's Clear all filters, which turns Show selected only off too. */
  clearAllFilters(): void {
    this.list.clearFilters();
    this.cdr.detectChanges();
    this.element('#mr-runs-search')?.focus();
  }

  showMore(): void {
    this.focusCard(this.list.showMore(this.availableRuns));
  }

  showAll(): void {
    this.focusCard(this.list.showAll(this.availableRuns));
  }

  /** Renders, then focuses the title of the card at `index` in the view, if there is one. */
  private focusCard(index: number): void {
    this.cdr.detectChanges();
    const run = this.cards[index];
    if (run) {
      this.element(`#mr-run-${run.id}-title`)?.focus();
    }
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }

  // --- A card ----------------------------------------------------------------------------------------

  /** What describes a card's checkbox: its reason line while the run cannot join. */
  checkboxDescribedBy(run: BenchmarkRunSummaryDto): string | null {
    return isSettledRun(run) ? null : `mr-run-${run.id}-reason`;
  }

  /** An index as the card shows it: whole numbers plain, others to one decimal, else an em dash. */
  formatIndex(value: number | null | undefined): string {
    if (value == null || !Number.isFinite(value)) return '—';
    return Number.isInteger(value) ? String(value) : value.toFixed(1);
  }

  runIndexOf(run: BenchmarkRunSummaryDto): number | null {
    return runIndex(run);
  }

  /** `2026-09-06 10:00 UTC`, or an em dash. */
  formatUtc(value: string | null | undefined): string {
    const iso = this.isoDate(value);
    return iso ? `${iso.slice(0, 10)} ${iso.slice(11, 16)} UTC` : '—';
  }

  isoDate(value: string | null | undefined): string | null {
    if (!value) return null;
    const date = parseServerUtcDate(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  /** `#41, #42`, for a differing key's variant. */
  runIdsLabel(runIds: readonly number[] | null | undefined): string {
    if (!runIds || runIds.length === 0) return '—';
    return runIds.map(id => `#${id}`).join(', ');
  }

  /** The first eight hex characters of an instrument hash, as the run list shows them. */
  shortFingerprint(sha: string | null | undefined): string {
    return sha ? sha.substring(0, 8) : '—';
  }

  fingerprintLongName(entry: BenchmarkFingerprintEntry): string {
    return FINGERPRINT_LONG_NAMES[entry.label];
  }

  /**
   * The five fingerprints of a run's instrument, in a fixed order. A hash that was never recorded
   * shows as a dash under its own label rather than dropping out, so two runs' rows line up.
   */
  fingerprintEntries(run: BenchmarkRunSummaryDto): BenchmarkFingerprintEntry[] {
    return [
      {
        label: 'PROMPT',
        cssClass: 'fp-prompt',
        short: this.shortFingerprint(run.candidateSystemPromptSha256),
        title: 'Candidate system prompt SHA-256: ' + (run.candidateSystemPromptSha256 || 'not recorded')
      },
      {
        label: 'GUIDES',
        cssClass: 'fp-guides',
        short: this.shortFingerprint(run.toolGuidesSha256),
        title: 'Tool guides SHA-256: ' + (run.toolGuidesSha256 || 'not recorded')
      },
      {
        label: 'KB',
        cssClass: 'fp-kb',
        short: this.shortFingerprint(run.knowledgeBaseHeadSha),
        title: 'Knowledge base Git HEAD SHA: ' + (run.knowledgeBaseHeadSha || 'not recorded')
      },
      {
        label: 'WIKI',
        cssClass: 'fp-wiki',
        short: this.shortFingerprint(run.wikiHeadSha),
        title: 'GnollHack wiki Git HEAD SHA: ' + (run.wikiHeadSha || 'not recorded')
      },
      {
        label: 'SRC',
        cssClass: 'fp-source',
        short: this.shortFingerprint(run.sourceCodeHeadSha),
        title: 'GnollHack source Git HEAD SHA: ' + (run.sourceCodeHeadSha || 'not recorded')
      }
    ];
  }

  /** A fingerprint button's tooltip id, unique per run and label. */
  fingerprintTipId(run: BenchmarkRunSummaryDto, entry: BenchmarkFingerprintEntry): string {
    return `mr-run-${run.id}-fp-${entry.label.toLowerCase()}-tip`;
  }
}
