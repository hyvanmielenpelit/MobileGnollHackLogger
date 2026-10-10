import {
  ChangeDetectionStrategy,
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
import { Subscription } from 'rxjs';

import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { CardListChip, CardListFacet, CardListSort, CardListState } from '../../../../shared/data-table/card-list-state';
import { FilterFacetComponent } from '../../../../shared/data-table/filter-facet.component';
import { TableState, anyOfFilter, customFilter } from '../../../../shared/data-table/table-state';
import { InfoTipComponent } from '../../../../shared/info-tip/info-tip.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import { formatUtcDate, formatUtcDateTime, plural, utcMillis } from '../chat-consistency-format';
import { CcCompareKind, ccCompareKindOf } from '../chat-consistency-results';
import { CC_CURRENT_ANALYSIS_CODE_VERSION, CcAnalysisSummary, CcModelAxis } from '../chat-consistency.models';
import { CcEndpointChipsComponent } from '../endpoint-chips/endpoint-chips.component';
import { CcBadgedModel, CcModelBadgesComponent } from '../model-badges/model-badges.component';

/** Where the history's Sort by order is kept, per browser. */
export const CC_HISTORY_VIEW_STORAGE_KEY = 'overseer.benchmark.chatConsistency.history.view';

/** The orders Sort by offers. */
export const CC_HISTORY_SORTS: readonly CardListSort[] = [
  { id: 'newest', label: 'Newest first', column: 'saved', direction: 'desc' },
  { id: 'oldest', label: 'Oldest first', column: 'saved', direction: 'asc' },
  { id: 'model', label: 'Model (A–Z)', column: 'model', direction: 'asc' }
];

/** How the subtitle names each order. */
const SORT_PHRASES: Readonly<Record<string, string>> = { newest: 'newest first', oldest: 'oldest first', model: 'by model' };

/** The Compared facet's values, in this order. */
const COMPARED_ORDER = ['Battery', 'Suite', 'All suites'];
const HAS_REPORTS = 'Has reports';
const NO_REPORTS = 'No reports';

/** The reason Delete is refused while report documents of the analysis exist. */
export function ccHistoryDeleteBlockedReason(analysis: CcAnalysisSummary): string {
  const count = analysis.reportDocumentCount;
  return `Delete its ${count === 1 ? 'report document' : `${count} report documents`} in step 6 first.`;
}

/**
 * The Analysis History dialog: every saved Chat Consistency analysis as a filterable card list, with
 * search, Sort by, the facets Model, Provider, Compared and Reports, and per card **Open** (the host
 * shows it in the wizard) and **Delete** (a nested confirmation; refused while report documents
 * exist, with a 409 from the server shown in the confirmation as well).
 */
@Component({
  selector: 'app-cc-analysis-history-dialog',
  standalone: true,
  imports: [FilterFacetComponent, InfoTipComponent, CcModelBadgesComponent, CcEndpointChipsComponent],
  templateUrl: './analysis-history-dialog.component.html',
  styleUrls: ['./analysis-history-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcAnalysisHistoryDialogComponent implements OnInit, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** The saved analyses, newest first, as the server lists them. */
  @Input() analyses: readonly CcAnalysisSummary[] = [];
  /** The model axes, for the name of a summary that does not name its model. */
  @Input() axes: readonly CcModelAxis[] = [];
  @Input() loading = false;
  @Input() error: string | null = null;
  /** The analysis being opened, for its Open button's busy state. */
  @Input() openingId: number | null = null;
  /** Why the last open started here failed. */
  @Input() openError: string | null = null;

  /** Open this analysis in the wizard; the host closes the dialog before the wizard opens. */
  @Output() readonly open = new EventEmitter<number>();
  @Output() readonly deleted = new EventEmitter<number>();

  @ViewChild('historyDialog') historyDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('historyTitle') historyTitle?: ElementRef<HTMLElement>;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;

  readonly sorts = CC_HISTORY_SORTS;
  readonly currentCodeVersion = CC_CURRENT_ANALYSIS_CODE_VERSION;
  readonly blockedReason = ccHistoryDeleteBlockedReason;

  deleteTarget: CcAnalysisSummary | null = null;
  deleting = false;
  deleteError: string | null = null;
  announcement = '';

  readonly table = new TableState<CcAnalysisSummary>('saved', 'desc').registerAccessors(
    {
      saved: a => utcMillis(a.createdAtUtc) || 0,
      model: a => this.modelName(a).toLowerCase()
    },
    {
      search: customFilter((a, value) => this.searchText(a).includes(value.trim().toLowerCase())),
      model: anyOfFilter(a => this.modelName(a)),
      provider: anyOfFilter(a => this.providerOf(a)),
      compared: anyOfFilter(a => ccCompareKindOf(a.comparisonSetKey).text),
      reports: anyOfFilter(a => (a.reportDocumentCount > 0 ? HAS_REPORTS : NO_REPORTS))
    }
  );

  /** The card list over `table`: the search, Sort by, the facets, the chips and the batch. */
  readonly list = new CardListState<CcAnalysisSummary>(this.table, {
    idPrefix: 'cc-hist',
    sorts: CC_HISTORY_SORTS,
    defaultSort: 'newest',
    storageKey: CC_HISTORY_VIEW_STORAGE_KEY,
    facets: [
      { column: 'model', label: 'Model', values: a => this.modelName(a) },
      { column: 'provider', label: 'Provider', values: a => this.providerOf(a) },
      { column: 'compared', label: 'Compared', values: a => ccCompareKindOf(a.comparisonSetKey).text, order: COMPARED_ORDER },
      { column: 'reports', label: 'Reports', values: a => (a.reportDocumentCount > 0 ? HAS_REPORTS : NO_REPORTS), order: [HAS_REPORTS, NO_REPORTS] }
    ],
    // The model and provider names come from the axes when a summary does not carry its model.
    memoDeps: () => [this.axes],
    onChange: () => this.cdr.markForCheck()
  });

  private deleteSub: Subscription | null = null;
  private returnFocus: HTMLElement | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.list.dispose();
    this.deleteSub?.unsubscribe();
    const dialog = this.historyDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  // --- The dialog ---

  get isOpen(): boolean {
    return this.historyDialog?.nativeElement.open ?? false;
  }

  /** Opens the dialog with its title focused, so the close button's tooltip does not open by itself. */
  show(): void {
    this.announcement = '';
    this.cdr.detectChanges();
    const dialog = this.historyDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
    refreshAnchorPositioning();
    this.historyTitle?.nativeElement.focus();
  }

  close(): void {
    const dialog = this.historyDialog?.nativeElement;
    if (dialog?.open) dialog.close();
  }

  /**
   * Light dismiss where `closedby` is unsupported: a backdrop click reports the dialog itself as the
   * target, so a hit outside its border box closes it.
   */
  onDialogClick(event: MouseEvent): void {
    if ('closedBy' in HTMLDialogElement.prototype) return;
    const dialog = this.historyDialog?.nativeElement;
    if (!dialog || event.target !== dialog) return;
    const rect = dialog.getBoundingClientRect();
    const inside = rect.top <= event.clientY && event.clientY <= rect.top + rect.height
      && rect.left <= event.clientX && event.clientX <= rect.left + rect.width;
    if (!inside) dialog.close();
  }

  /** The nested confirmation's events stop at it, short of this dialog and the tab around it. */
  stopNested(event: Event): void {
    event.stopPropagation();
  }

  // --- The list ---

  get cards(): CcAnalysisSummary[] {
    return this.list.view(this.analyses);
  }

  get facets(): CardListFacet[] {
    return this.list.facets(this.analyses);
  }

  get chips(): CardListChip[] {
    return this.list.chips(this.analyses);
  }

  /** The filter bar is shown with two analyses or more, and while a filter is still active. */
  get showFilterBar(): boolean {
    return this.analyses.length > 1 || this.table.hasActiveFilters;
  }

  get listStatus(): string {
    if (this.loading) return 'Loading the saved analyses…';
    if (this.error) return '';
    const text = this.list.statusText(this.analyses, { one: 'analysis', many: 'analyses' });
    return [text, this.announcement].filter(part => part !== '').join(' · ');
  }

  /** `4 saved analyses · newest first`. */
  get subtitle(): string {
    if (this.loading && this.analyses.length === 0) return 'Loading the saved analyses…';
    const count = plural(this.analyses.length, 'saved analysis', 'saved analyses');
    const order = SORT_PHRASES[this.list.sortId];
    return this.analyses.length > 1 && order ? `${count} · ${order}` : count;
  }

  get noMatches(): boolean {
    return this.table.noMatches(this.analyses);
  }

  get remainingCount(): number {
    return this.list.remainingCount(this.analyses);
  }

  get nextBatchCount(): number {
    return this.list.nextBatchCount(this.analyses);
  }

  get matchingCount(): number {
    return this.list.matching(this.analyses).length;
  }

  onSearchInput(event: Event): void {
    this.list.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once and keeps the dialog open; in an empty field it closes the dialog. */
  onSearchKeydown(event: KeyboardEvent): void {
    if (this.list.clearSearchOnEscape(event)) this.cdr.detectChanges();
  }

  onSortChange(event: Event): void {
    if (this.list.setSort((event.target as HTMLSelectElement).value)) this.cdr.detectChanges();
  }

  onFacetChange(column: string, values: string[]): void {
    this.list.setFacet(column, values);
    this.cdr.detectChanges();
  }

  /** Removes a chip's filter, then focuses the chip now in its place, else the previous one, else the search. */
  removeChip(chip: CardListChip): void {
    const index = this.list.removeChip(chip, this.analyses);
    this.cdr.detectChanges();
    const chips = Array.from(this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.cc-hist-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? this.element('#cc-hist-search'))?.focus();
  }

  /** Clears the search and every filter, then focuses the search. */
  clearFilters(): void {
    this.list.clearFilters();
    this.cdr.detectChanges();
    this.element('#cc-hist-search')?.focus();
  }

  showMore(): void {
    this.focusCard(this.list.showMore(this.analyses));
  }

  showAll(): void {
    this.focusCard(this.list.showAll(this.analyses));
  }

  // --- A card ---

  /** The model's name: the summary's own, else its axis's, else its key. */
  modelName(analysis: CcAnalysisSummary): string {
    return analysis.subject?.displayName
      ?? this.axes.find(axis => axis.key === analysis.subjectModelKey)?.displayName
      ?? analysis.subjectModelKey;
  }

  /** The model's badges, or null when neither the summary nor an axis names the model. */
  badgesOf(analysis: CcAnalysisSummary): CcBadgedModel | null {
    return analysis.subject ?? this.axes.find(axis => axis.key === analysis.subjectModelKey) ?? null;
  }

  title(analysis: CcAnalysisSummary): string {
    return analysis.name?.trim() || `Analysis #${analysis.id}`;
  }

  savedText(analysis: CcAnalysisSummary): string {
    return formatUtcDateTime(analysis.createdAtUtc);
  }

  periodText(startUtc: string, endUtc: string): string {
    const start = formatUtcDate(startUtc);
    const end = formatUtcDate(endUtc);
    return start === end ? start : `${start} – ${end}`;
  }

  compareKind(analysis: CcAnalysisSummary): CcCompareKind {
    return ccCompareKindOf(analysis.comparisonSetKey);
  }

  isEarlierCode(analysis: CcAnalysisSummary): boolean {
    return analysis.analysisCodeVersion < CC_CURRENT_ANALYSIS_CODE_VERSION;
  }

  earlierCodeText(analysis: CcAnalysisSummary): string {
    return `Saved under analysis code version ${analysis.analysisCodeVersion}. Analyze again to apply version ${CC_CURRENT_ANALYSIS_CODE_VERSION}.`;
  }

  reportsTag(analysis: CcAnalysisSummary): string {
    return plural(analysis.reportDocumentCount, 'report');
  }

  onOpen(analysis: CcAnalysisSummary): void {
    if (this.openingId !== null) return;
    this.open.emit(analysis.id);
  }

  // --- Delete ---

  /** Asks first; refused while report documents of the analysis exist. */
  requestDelete(analysis: CcAnalysisSummary, button: HTMLElement): void {
    if (this.deleting || analysis.reportDocumentCount > 0) return;
    this.deleteTarget = analysis;
    this.deleteError = null;
    this.announcement = '';
    this.returnFocus = button;
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) dialog.showModal();
  }

  cancelDelete(): void {
    this.deleteDialog?.nativeElement.close();
    this.returnFocus?.focus();
  }

  confirmDelete(): void {
    const target = this.deleteTarget;
    if (!target || this.deleting) return;
    const view = this.cards;
    const index = view.findIndex(analysis => analysis.id === target.id);
    this.deleting = true;
    this.deleteError = null;
    this.cdr.markForCheck();
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.service.deleteAnalysis(target.id).subscribe({
      next: () => {
        this.deleting = false;
        this.deleteDialog?.nativeElement.close();
        this.deleteTarget = null;
        this.announcement = `${this.title(target)} was deleted.`;
        this.deleted.emit(target.id);
        this.cdr.detectChanges();
        this.focusAfterDelete(view, index, target.id);
      },
      error: err => {
        this.deleting = false;
        // 409: report documents were written from it; the refusal says which and what to do.
        this.deleteError = ccErrorText(err, 'The analysis could not be deleted.');
        this.cdr.markForCheck();
      }
    });
  }

  // --- Focus ---

  /** Renders, then focuses the title of the card at `index` in the view, if there is one. */
  private focusCard(index: number): void {
    this.cdr.detectChanges();
    const analysis = this.cards[index];
    if (analysis) this.element(`#cc-hist-${analysis.id}-title`)?.focus();
  }

  /** The card now at the deleted one's place, else the previous one, else the dialog title. */
  private focusAfterDelete(view: readonly CcAnalysisSummary[], index: number, deletedId: number): void {
    const rest = view.filter(analysis => analysis.id !== deletedId);
    const next = index >= 0 ? rest[Math.min(index, rest.length - 1)] : undefined;
    const target = next ? this.element(`#cc-hist-${next.id}-title`) : null;
    (target ?? this.historyTitle?.nativeElement)?.focus();
  }

  private element(selector: string): HTMLElement | null {
    return this.host.nativeElement.querySelector<HTMLElement>(selector);
  }

  /** What the search matches: the name, the headline, the model and `#id`. */
  private searchText(analysis: CcAnalysisSummary): string {
    return [analysis.name ?? '', analysis.headline ?? '', this.modelName(analysis), `#${analysis.id}`].join('\n').toLowerCase();
  }

  private providerOf(analysis: CcAnalysisSummary): string | null {
    return analysis.subject?.provider ?? this.axes.find(axis => axis.key === analysis.subjectModelKey)?.provider ?? null;
  }
}
