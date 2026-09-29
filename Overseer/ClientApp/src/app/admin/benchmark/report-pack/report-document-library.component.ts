import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import type { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPeerNaming,
  reportDisclosureParam,
  reportPeerNamingParam
} from '../../../services/admin-benchmark.service';
import { TableState, exactFilter } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import {
  BenchmarkDownloadCenterComponent,
  audienceLabel,
  rememberedPdfPaper,
  reportDocumentFileStem
} from '../download-center/benchmark-download-center.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';

/** Which documents the library lists: those of one comparison, or every Report Pack document. */
export type ReportDocumentLibraryScope =
  | { readonly kind: 'comparison'; readonly entryKeys: readonly string[] }
  | { readonly kind: 'all' };

/** How many documents the `all` scope asks for: the list endpoint's maximum. */
export const REPORT_LIBRARY_ALL_TAKE = 500;

export function disclosureLabel(disclosure: BenchmarkReportDisclosure): string {
  switch (disclosure) {
    case BenchmarkReportDisclosure.Detailed: return 'Detailed';
    case BenchmarkReportDisclosure.Full: return 'Full';
    default: return 'Summary';
  }
}

/** `CompletedWithWarnings` → `Completed with warnings`. */
export function statusLabel(status: string | null | undefined): string {
  if (!status) {
    return '';
  }
  const words = status.replace(/([a-z])([A-Z])/g, '$1 $2').split(' ');
  return words.map((word, i) => (i === 0 ? word : word.toLowerCase())).join(' ');
}

/** A stored document's status as the AI Reports tab words it: *Written*, *Written with warnings*. */
export function documentStatusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'Completed': return 'Written';
    case 'CompletedWithWarnings': return 'Written with warnings';
    default: return statusLabel(status);
  }
}

export function formatCostUsd(cost: number | null | undefined): string {
  if (cost === null || cost === undefined || !Number.isFinite(cost)) {
    return 'Unknown';
  }
  if (cost === 0) {
    return '$0.00';
  }
  return cost < 0.01 ? `$${cost.toFixed(4)}` : `$${cost.toFixed(2)}`;
}

/** `2026-09-21 16:00 UTC`, from an ISO timestamp. */
export function formatUtc(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return `${date.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}

/** `4 other models`, `1 other model`, `No other models`. */
export function peerCountLabel(count: number): string {
  if (count <= 0) {
    return 'No other models';
  }
  return `${count} other ${count === 1 ? 'model' : 'models'}`;
}

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

/** The `{ error }` or plain-string message of a refused request, or null. */
function serverMessage(error: HttpErrorResponse): string | null {
  const body = error?.error;
  if (typeof body === 'string' && body.trim()) {
    return body.trim();
  }
  if (body && typeof body === 'object') {
    const message = (body as { error?: unknown; message?: unknown }).error ?? (body as { message?: unknown }).message;
    if (typeof message === 'string' && message.trim()) {
      return message.trim();
    }
  }
  return null;
}

/**
 * The stored report documents of a comparison, or of every comparison: a paged, sortable and
 * filterable table with selection by id, and per row View (the PDF viewer), Download (the Download
 * Center) and Delete (behind a confirmation). Embedded by the Report Pack dialog and by the Model
 * Comparison launcher.
 *
 * It loads on init and whenever `scope` changes its entries or `reloadToken` changes. Its nested
 * dialogs stop their own close and cancel events, so a dialog around the library never sees them;
 * it works outside any dialog as well. Every element id derives from `idPrefix`, so two libraries
 * can live in one document.
 */
@Component({
  selector: 'app-report-document-library',
  standalone: true,
  imports: [SortHeaderComponent, TablePagerComponent, PdfViewerDialogComponent, BenchmarkDownloadCenterComponent],
  templateUrl: './report-document-library.component.html',
  styleUrls: ['./report-document-library.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportDocumentLibraryComponent implements OnInit, OnChanges, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  /** The documents to list. */
  @Input() scope: ReportDocumentLibraryScope = { kind: 'all' };
  /** The library's own heading; none when empty, for a host that heads the section itself. */
  @Input() heading = '';
  /** The prefix of every element id: `<idPrefix>-doc-<id>-view` and the like. */
  @Input() idPrefix = 'rdl';
  /** Shows *Compared with*: the peer count and the suite. */
  @Input() showComparisonColumn = false;
  /** Bumped by the host to list the documents again, as after a job. */
  @Input() reloadToken = 0;

  /** Every document the library holds, after each load and each delete. */
  @Output() readonly documentsChange = new EventEmitter<readonly BenchmarkReportDocumentListItemDto[]>();

  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild(PdfViewerDialogComponent) pdfViewer?: PdfViewerDialogComponent;
  @ViewChild(BenchmarkDownloadCenterComponent) downloadCenter?: BenchmarkDownloadCenterComponent;

  readonly formatUtc = formatUtc;
  readonly formatCostUsd = formatCostUsd;
  readonly documentStatusLabel = documentStatusLabel;
  readonly audienceLabel = audienceLabel;
  readonly peerCountLabel = peerCountLabel;

  documents: BenchmarkReportDocumentListItemDto[] = [];
  /** The list has answered at least once for the current scope. */
  loaded = false;
  loading = false;
  loadError: string | null = null;
  /** A one-off confirmation read out by the status line. */
  status = '';

  readonly selectedIds = new Set<number>();
  readonly table = new TableState<BenchmarkReportDocumentListItemDto>('createdAtUtc', 'desc').registerAccessors(
    {
      createdAtUtc: d => d.createdAtUtc,
      subject: d => d.subjectLabel,
      document: d => audienceLabel(d.audience),
      writer: d => d.writerDisplayName,
      status: d => documentStatusLabel(d.status),
      cost: d => d.costUsd ?? null
    },
    {
      subject: d => d.subjectLabel,
      document: exactFilter(d => audienceLabel(d.audience)),
      suite: exactFilter(d => d.suiteName),
      selected: exactFilter(d => (this.selectedIds.has(d.id) ? 'yes' : 'no'))
    }
  );

  deleteTarget: BenchmarkReportDocumentListItemDto | null = null;
  deleting = false;
  deleteError: string | null = null;

  /** Where focus returns when a nested dialog closes. */
  private returnFocus: HTMLElement | null = null;
  private scopeKey: string | null = null;
  private generation = 0;
  private listSub: Subscription | null = null;
  private deleteSub: Subscription | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
    if (this.scopeKey === null) {
      this.reload();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    const scopeChanged = !!changes['scope'] && this.keyOf(this.scope) !== this.scopeKey;
    const tokenChanged = !!changes['reloadToken'] && !changes['reloadToken'].firstChange;
    if (scopeChanged) {
      this.selectedIds.clear();
      this.table.clearFilters();
      this.table.page = 1;
      this.documents = [];
      this.loaded = false;
      this.status = '';
    }
    if (scopeChanged || tokenChanged) {
      this.reload();
    }
  }

  ngOnDestroy(): void {
    this.generation++;
    this.listSub?.unsubscribe();
    this.deleteSub?.unsubscribe();
  }

  // -------------------------------------------------------------------------------------------
  // Loading
  // -------------------------------------------------------------------------------------------

  /** Lists the scope's documents again; a list in flight is dropped. */
  reload(): void {
    const scope = this.scope;
    this.scopeKey = this.keyOf(scope);
    const generation = ++this.generation;
    this.loading = true;
    this.loadError = null;
    this.listSub?.unsubscribe();
    const query = scope.kind === 'comparison'
      ? { comparison: scope.entryKeys, origin: 'reportPack' as const }
      : { origin: 'reportPack' as const, take: REPORT_LIBRARY_ALL_TAKE };
    this.listSub = this.benchmarkService.listReportDocuments(query).subscribe({
      next: documents => {
        if (generation !== this.generation) {
          return;
        }
        this.loading = false;
        this.loaded = true;
        this.documents = documents ?? [];
        const present = new Set(this.documents.map(d => d.id));
        for (const id of [...this.selectedIds]) {
          if (!present.has(id)) {
            this.selectedIds.delete(id);
          }
        }
        this.documentsChange.emit(this.documents);
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.loading = false;
        this.loaded = true;
        this.loadError = serverMessage(error) ?? 'The report documents could not be loaded.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  private keyOf(scope: ReportDocumentLibraryScope | null | undefined): string {
    return scope?.kind === 'comparison' ? `comparison:${scope.entryKeys.join(',')}` : 'all';
  }

  // -------------------------------------------------------------------------------------------
  // The table
  // -------------------------------------------------------------------------------------------

  get isAllScope(): boolean {
    return this.scope?.kind !== 'comparison';
  }

  get view(): BenchmarkReportDocumentListItemDto[] {
    return this.table.view(this.documents);
  }

  /** Every document the filters let through, on any page, in the table's order. */
  get filteredDocuments(): BenchmarkReportDocumentListItemDto[] {
    return this.table.viewAll(this.documents);
  }

  get emptyText(): string {
    return this.isAllScope ? 'No comparison reports yet.' : 'No reports have been written for this comparison yet.';
  }

  /** The Document filter's options: the document types present. */
  get documentOptions(): string[] {
    return [...new Set(this.documents.map(d => audienceLabel(d.audience)))]
      .sort((a, b) => this.audienceOrder(a) - this.audienceOrder(b));
  }

  /** The Suite filter's options: the suites present. */
  get suiteOptions(): string[] {
    return [...new Set(this.documents.map(d => d.suiteName).filter(name => !!name))]
      .sort((a, b) => a.localeCompare(b));
  }

  private audienceOrder(label: string): number {
    const order = [BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief]
      .map(audienceLabel);
    const index = order.indexOf(label);
    return index < 0 ? order.length : index;
  }

  filterValue(column: string): string {
    return this.table.filters[column] ?? '';
  }

  setFilter(column: string, event: Event): void {
    this.table.setFilter(column, (event.target as HTMLInputElement | HTMLSelectElement).value);
    this.cdr.markForCheck();
  }

  clearFilters(): void {
    this.table.clearFilters();
    this.cdr.markForCheck();
  }

  onTableChanged(): void {
    this.cdr.markForCheck();
  }

  /** The row's name in every control's accessible name. */
  documentName(doc: BenchmarkReportDocumentListItemDto): string {
    return `${doc.title}, ${formatUtc(doc.createdAtUtc)}`;
  }

  peerCountOf(doc: BenchmarkReportDocumentListItemDto): number {
    return doc.peerCount ?? 0;
  }

  // -------------------------------------------------------------------------------------------
  // Selection
  // -------------------------------------------------------------------------------------------

  isSelected(doc: BenchmarkReportDocumentListItemDto): boolean {
    return this.selectedIds.has(doc.id);
  }

  onSelectChange(doc: BenchmarkReportDocumentListItemDto, event: Event): void {
    if ((event.target as HTMLInputElement).checked) {
      this.selectedIds.add(doc.id);
    } else {
      this.selectedIds.delete(doc.id);
    }
    this.cdr.markForCheck();
  }

  clearSelection(): void {
    this.selectedIds.clear();
    if (this.showSelectedOnly) {
      this.table.setFilter('selected', '');
    }
    this.cdr.markForCheck();
  }

  /** Selected documents the current page does not show. */
  get offPageSelectedCount(): number {
    const onPage = new Set(this.view.map(d => d.id));
    return [...this.selectedIds].filter(id => !onPage.has(id)).length;
  }

  get showSelectedOnly(): boolean {
    return this.table.filters['selected'] === 'yes';
  }

  toggleShowSelectedOnly(): void {
    this.table.setFilter('selected', this.showSelectedOnly ? '' : 'yes');
    this.cdr.markForCheck();
  }

  /** The selected ids in the list's order. */
  get selectedDocumentIds(): number[] {
    return this.documents.filter(d => this.selectedIds.has(d.id)).map(d => d.id);
  }

  // -------------------------------------------------------------------------------------------
  // Download
  // -------------------------------------------------------------------------------------------

  /** Download… is unavailable only while the view holds no document. */
  get downloadDisabled(): boolean {
    return this.selectedIds.size === 0 && this.filteredDocuments.length === 0;
  }

  /** Download…'s accessible name: what it opens the Download Center on. */
  get downloadLabel(): string {
    const selected = this.selectedIds.size;
    if (selected > 0) {
      return `Download ${plural(selected, 'selected document', 'selected documents')}`;
    }
    const shown = this.filteredDocuments.length;
    if (shown === 0) {
      return 'Download documents: none shown';
    }
    return shown === 1 ? 'Download the 1 document shown' : `Download all ${shown} documents shown`;
  }

  /** The selected documents, or with none selected every document the filters show. */
  downloadFromToolbar(button: HTMLElement): void {
    if (this.downloadDisabled) {
      return;
    }
    const ids = this.selectedIds.size > 0 ? this.selectedDocumentIds : this.filteredDocuments.map(d => d.id);
    this.openDownloadCenter(ids, button);
  }

  downloadDocument(doc: BenchmarkReportDocumentListItemDto, button: HTMLElement): void {
    this.openDownloadCenter([doc.id], button);
  }

  /** Opens the Download Center on every document the library holds, filters aside. */
  openDownloadCenterForAll(returnFocus: HTMLElement | null = null): void {
    this.openDownloadCenter(this.documents.map(d => d.id), returnFocus);
  }

  private openDownloadCenter(documentIds: number[], returnFocus: HTMLElement | null): void {
    if (documentIds.length === 0) {
      return;
    }
    this.returnFocus = returnFocus;
    this.downloadCenter?.open({
      kind: 'documents',
      documentIds,
      title: 'Comparison reports',
      subtitle: this.downloadSubtitle(documentIds.length)
    });
  }

  private downloadSubtitle(count: number): string {
    const scope = this.scope;
    if (scope?.kind === 'comparison') {
      return `${plural(count, 'document', 'documents')} of the comparison of ${plural(scope.entryKeys.length, 'model', 'models')}`;
    }
    return plural(count, 'comparison report document', 'comparison report documents');
  }

  // -------------------------------------------------------------------------------------------
  // View
  // -------------------------------------------------------------------------------------------

  /**
   * Opens a document in the PDF viewer at the fullest disclosure it allows, the others offered as
   * versions; with peers, the peer names are a second choice that opens at *Named*.
   */
  viewDocument(doc: BenchmarkReportDocumentListItemDto, button: HTMLElement): void {
    this.returnFocus = button;
    const allowed = [...new Set(doc.allowedDisclosures ?? [])].sort((a, b) => a - b);
    const disclosures = allowed.length > 0 ? allowed : [BenchmarkReportDisclosure.Full];
    const highest = disclosures[disclosures.length - 1];
    const byKey = new Map<string, BenchmarkReportDisclosure>(
      disclosures.map(disclosure => [reportDisclosureParam(disclosure), disclosure] as const));
    const disclosureOf = (variant: string | null): BenchmarkReportDisclosure =>
      (variant !== null ? byKey.get(variant) : undefined) ?? highest;
    const namingOf = (secondary: string | undefined): BenchmarkReportPeerNaming =>
      secondary === reportPeerNamingParam(BenchmarkReportPeerNaming.Anonymized)
        ? BenchmarkReportPeerNaming.Anonymized
        : BenchmarkReportPeerNaming.Named;
    const hasPeers = this.peerCountOf(doc) > 0;
    const paper = rememberedPdfPaper();
    const label = audienceLabel(doc.audience);
    this.pdfViewer?.open({
      title: doc.title || label,
      subtitle: this.viewerSubtitle(doc),
      variants: disclosures.map(disclosure => ({ key: reportDisclosureParam(disclosure), label: disclosureLabel(disclosure) })),
      initialVariant: reportDisclosureParam(highest),
      variantsInfo: reportDisclosureInfo(doc.audience, { peerNaming: hasPeers }),
      ...(hasPeers ? {
        secondaryVariants: {
          label: 'Peer names',
          options: [BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized].map(naming => ({
            key: reportPeerNamingParam(naming),
            label: naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized'
          })),
          initial: reportPeerNamingParam(BenchmarkReportPeerNaming.Named)
        }
      } : {}),
      load: (variant, secondary) =>
        this.benchmarkService.getReportDocumentPdf(doc.id, disclosureOf(variant), namingOf(secondary), paper),
      tabUrl: (variant, secondary) =>
        this.benchmarkService.reportDocumentPdfUrl(doc.id, disclosureOf(variant), namingOf(secondary), paper, true),
      fallbackFileName: `${reportDocumentFileStem(doc, label)}.pdf`
    });
  }

  /** `<subject> · compared with N models · by <writer> on <date>`. */
  viewerSubtitle(doc: BenchmarkReportDocumentListItemDto): string {
    const parts = [doc.subjectLabel];
    const peers = this.peerCountOf(doc);
    if (peers > 0) {
      parts.push(`compared with ${plural(peers, 'model', 'models')}`);
    }
    parts.push(`by ${doc.writerDisplayName || 'the report writer'} on ${formatUtc(doc.createdAtUtc) || 'an unknown date'}`);
    return parts.join(' · ');
  }

  // -------------------------------------------------------------------------------------------
  // Delete
  // -------------------------------------------------------------------------------------------

  requestDelete(doc: BenchmarkReportDocumentListItemDto, button: HTMLElement): void {
    this.deleteTarget = doc;
    this.deleteError = null;
    this.deleting = false;
    this.returnFocus = button;
    this.cdr.detectChanges();
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  cancelDelete(): void {
    this.deleteDialog?.nativeElement?.close();
  }

  confirmDelete(): void {
    const doc = this.deleteTarget;
    if (!doc || this.deleting) {
      return;
    }
    this.deleting = true;
    this.deleteError = null;
    const generation = this.generation;
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.benchmarkService.deleteReportDocument(doc.id).subscribe({
      next: () => {
        if (generation !== this.generation) {
          return;
        }
        this.deleting = false;
        this.documents = this.documents.filter(d => d.id !== doc.id);
        this.selectedIds.delete(doc.id);
        this.status = `Deleted ${this.documentName(doc)}.`;
        // The row's own button is gone; Download… stays.
        this.returnFocus = document.getElementById(`${this.idPrefix}-download`);
        this.deleteDialog?.nativeElement?.close();
        this.documentsChange.emit(this.documents);
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.deleting = false;
        this.deleteError = serverMessage(error) ?? 'The document could not be deleted.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  onDeleteDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.deleteDialog?.nativeElement?.open) {
      return;
    }
    this.deleteTarget = null;
    this.deleteError = null;
    this.cdr.markForCheck();
    this.restoreFocus();
  }

  // -------------------------------------------------------------------------------------------
  // Nested dialogs
  // -------------------------------------------------------------------------------------------

  /** The nested dialogs' close and cancel events stop here, short of any dialog around the library. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  /** The PDF viewer or the Download Center closed: focus goes back to the button that opened it. */
  onNestedClosed(): void {
    this.restoreFocus();
  }

  private restoreFocus(): void {
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target?.isConnected) {
      target.focus();
    }
  }
}
