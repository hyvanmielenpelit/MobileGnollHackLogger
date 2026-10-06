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
import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackWrittenDocumentDto,
  BenchmarkReportPeerNaming,
  BenchmarkReportScope,
  reportDisclosureParam,
  reportPeerNamingParam
} from '../../../services/admin-benchmark.service';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { rememberedPdfPaper, reportDocumentFileStem } from '../download-center/download-center-panel.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import {
  MAX_COMPARISON_DOCUMENT_ENTRIES,
  MIN_COMPARISON_DOCUMENT_ENTRIES
} from './comparison-model-options';
import {
  REPORT_PACK_AUDIENCES,
  audienceLabel,
  disclosureLabel,
  documentStatusLabel,
  formatCostUsd,
  formatElapsed,
  formatUtc
} from './report-document-format';

/** Step 3's document scope: comparison-wide (or model subset) documents, or per-model ones. */
export type ReportDocumentScopeMode = 'comparison' | 'model';

/** One document of the comparison, written or not, and whether the next job writes it. */
export interface ComparisonDocumentRow {
  /** `comparison|<audience>` for the chosen set; `<entry key>|<audience>` for a model; `<set key>|<audience>` for another set. */
  readonly key: string;
  readonly audience: BenchmarkReportAudience;
  /** The document type's name. */
  readonly name: string;
  /** Whom the document is about: a model's label, or the models of a set. */
  readonly subjectLabel: string;
  /** The model a per-model row is about; null for a comparison-scope row. */
  readonly entryKey: string | null;
  readonly document: BenchmarkReportPackWrittenDocumentDto | null;
  /** The stored document's list entry, when the list has answered for it. */
  readonly listItem: BenchmarkReportDocumentListItemDto | null;
  /** The row has a Write or Rewrite checkbox. */
  readonly checkable: boolean;
  readonly checked: boolean;
}

/** The rows of one model, or of the chosen set (no heading). */
export interface ComparisonDocumentGroup {
  readonly key: string;
  readonly heading: string | null;
  readonly rows: readonly ComparisonDocumentRow[];
}

/** Another covered set of the comparison that has documents. */
export interface ComparisonOtherModelSet {
  readonly key: string;
  /** `GPT-5.6 Luna (max), GPT-6.1 Sol (medium) — 2 of 5 models`. */
  readonly label: string;
  readonly entryKeys: readonly string[];
  /** Why **Choose these models** cannot set the picker to this set, or null when it can. */
  readonly chooseRefusal: string | null;
  readonly rows: readonly ComparisonDocumentRow[];
}

/** What the list shows. */
export interface ComparisonDocumentsView {
  readonly mode: ReportDocumentScopeMode;
  /** `loading` until the preview has answered for the current mode and models. */
  readonly state: 'loading' | 'ready' | 'error';
  readonly message: string | null;
  readonly groups: readonly ComparisonDocumentGroup[];
  readonly otherSets: readonly ComparisonOtherModelSet[];
}

/** What the view is built from. */
export interface ComparisonDocumentsViewInput {
  readonly mode: ReportDocumentScopeMode;
  /** The preview's answer for the current mode and models; null while it is awaited. */
  readonly preview: BenchmarkReportPackPreviewDto | null;
  /** Why the preview failed, or null. */
  readonly error: string | null;
  /** The models chosen, in the picker's order. */
  readonly chosenKeys: readonly string[];
  /** Every entry the picker offers. */
  readonly offeredKeys: readonly string[];
  /** A model's label, by entry key. */
  readonly labelOf: (entryKey: string) => string;
  readonly listItems: ReadonlyMap<number, BenchmarkReportDocumentListItemDto>;
  /** The admin's own choices of Write and Rewrite, by row key; a row without one takes its default. */
  readonly checks: ReadonlyMap<string, boolean>;
}

/** Not written: checked. Written: unchecked, unless the admin chose Rewrite. */
export function defaultRowChecked(written: boolean): boolean {
  return !written;
}

function rowOf(
  key: string,
  audience: BenchmarkReportAudience,
  subjectLabel: string,
  entryKey: string | null,
  document: BenchmarkReportPackWrittenDocumentDto | null,
  input: ComparisonDocumentsViewInput,
  checkable: boolean
): ComparisonDocumentRow {
  return {
    key,
    audience,
    name: audienceLabel(audience),
    subjectLabel,
    entryKey,
    document,
    listItem: document ? input.listItems.get(document.documentId) ?? null : null,
    checkable,
    checked: checkable && (input.checks.get(key) ?? defaultRowChecked(document !== null))
  };
}

/** `2 of 5 models`, or `all 5 models` for a set written over every model. */
function setSizeText(count: number, of: number | undefined, all: boolean): string {
  if (all) {
    return `all ${count} models`;
  }
  return of ? `${count} of ${of} models` : `${count} models`;
}

/**
 * The list for the current mode and models. Comparison scope: one row per document type for the chosen
 * set, then every other set that has documents. Per-model scope: one group per chosen model, one row
 * per document type.
 */
export function comparisonDocumentsView(input: ComparisonDocumentsViewInput): ComparisonDocumentsView {
  const preview = input.preview;
  if (!preview) {
    return {
      mode: input.mode,
      state: input.error ? 'error' : 'loading',
      message: input.error,
      groups: [],
      otherSets: []
    };
  }
  const audiences = REPORT_PACK_AUDIENCES.map(option => option.audience);
  if (input.mode === 'comparison') {
    const written = preview.writtenDocuments ?? [];
    const label = input.chosenKeys.map(key => input.labelOf(key)).join(', ');
    const rows = audiences.map(audience => rowOf(
      `comparison|${audience}`, audience, label, null, written.find(doc => doc.audience === audience) ?? null, input, true));
    const offered = new Set(input.offeredKeys);
    const otherSets = (preview.otherModelSets ?? [])
      .filter(set => (set.documents ?? []).length > 0)
      .map((set): ComparisonOtherModelSet => {
        const models = set.coveredModels ?? [];
        const keys = models.map(model => model.entryKey);
        const setLabel = `${models.map(model => model.label).join(', ')} — `
          + setSizeText(models.length, preview.comparisonEntryCount, set.coversAllEntries);
        let chooseRefusal: string | null = null;
        if (keys.some(key => !offered.has(key))) {
          chooseRefusal = 'A model of this set is Excluded from the comparison now.';
        } else if (keys.length < MIN_COMPARISON_DOCUMENT_ENTRIES || keys.length > MAX_COMPARISON_DOCUMENT_ENTRIES) {
          chooseRefusal = `A document covers ${MIN_COMPARISON_DOCUMENT_ENTRIES} to ${MAX_COMPARISON_DOCUMENT_ENTRIES} models.`;
        }
        return {
          key: set.coveredSetKey,
          label: setLabel,
          entryKeys: keys,
          chooseRefusal,
          rows: audiences
            .map(audience => (set.documents ?? []).find(doc => doc.audience === audience) ?? null)
            .filter((doc): doc is BenchmarkReportPackWrittenDocumentDto => doc !== null)
            .map(doc => rowOf(`${set.coveredSetKey}|${doc.audience}`, doc.audience, setLabel, null, doc, input, false))
        };
      });
    return { mode: 'comparison', state: 'ready', message: null, groups: [{ key: 'chosen', heading: null, rows }], otherSets };
  }

  const subjects = preview.subjectDocuments ?? [];
  const groups = input.chosenKeys.map((entryKey): ComparisonDocumentGroup => {
    const documents = subjects.find(subject => subject.subjectKey === entryKey)?.documents ?? [];
    const heading = input.labelOf(entryKey);
    return {
      key: entryKey,
      heading,
      rows: audiences.map(audience => rowOf(
        `${entryKey}|${audience}`, audience, heading, entryKey, documents.find(doc => doc.audience === audience) ?? null, input, true))
    };
  });
  return { mode: 'model', state: 'ready', message: null, groups, otherSets: [] };
}

/** `by Claude 5.5 Opus (Anthropic; medium)`. */
export function writerPhrase(doc: Pick<BenchmarkReportPackWrittenDocumentDto, 'writerDisplayName' | 'writerProvider' | 'writerThinkingLevel'>): string {
  const details = [doc.writerProvider, doc.writerThinkingLevel]
    .map(part => (part ?? '').trim())
    .filter(part => part !== '');
  const name = doc.writerDisplayName?.trim() || 'an unknown writer';
  return details.length > 0 ? `by ${name} (${details.join('; ')})` : `by ${name}`;
}

/** `Charts: 3`, `No charts`; null while the list has not answered. */
export function chartsPhrase(item: BenchmarkReportDocumentListItemDto | null): string | null {
  if (!item) {
    return null;
  }
  const count = item.chartFigureKeys?.length ?? item.chartCount ?? 0;
  return count > 0 ? `Charts: ${count}` : 'No charts';
}

/** The comparison's runs, groups or battery runs changed since the document was written. */
export function comparisonChanged(item: BenchmarkReportDocumentListItemDto | null): boolean {
  return !!item && (item.runChangedSinceGeneration || !!item.peersChangedSinceGeneration);
}

/** The server's `{ error }` or plain-string message of a refused request, or null. */
function serverMessage(error: HttpErrorResponse): string | null {
  const body = error.error;
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
 * Step 3's *Documents of this comparison*: every document the chosen models can have, written or not,
 * with its writer, date, duration, cost and charts, a Write or Rewrite checkbox the host turns into
 * the next job, and icon-only View (the PDF viewer) and Delete (after a confirmation). In comparison
 * scope, the other covered sets with documents follow in a closed disclosure, each with
 * **Choose these models**.
 *
 * The host owns the rows and the checks; this component owns the viewer and the delete.
 */
@Component({
  selector: 'app-comparison-documents-status',
  standalone: true,
  imports: [NgTemplateOutlet, PdfViewerDialogComponent],
  templateUrl: './comparison-documents-status.component.html',
  styleUrls: ['./comparison-documents-status.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ComparisonDocumentsStatusComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @ViewChild(PdfViewerDialogComponent) pdfViewer?: PdfViewerDialogComponent;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;

  @Input() view: ComparisonDocumentsView | null = null;

  /** A job is being written: no check changes, and no delete. */
  @Input() busy = false;

  @Input() idPrefix = 'cds';

  /** A row's Write or Rewrite checkbox changed. */
  @Output() readonly checkChange = new EventEmitter<{ key: string; checked: boolean }>();

  /** **Choose these models**: the entry keys of another set. */
  @Output() readonly chooseModels = new EventEmitter<readonly string[]>();

  /** A document was deleted. */
  @Output() readonly documentDeleted = new EventEmitter<number>();

  readonly writerPhrase = writerPhrase;
  readonly chartsPhrase = chartsPhrase;
  readonly comparisonChanged = comparisonChanged;
  readonly formatUtc = formatUtc;
  readonly formatCostUsd = formatCostUsd;
  readonly busyReason = 'Wait for the report pack to finish, or cancel it.';

  deleteTarget: ComparisonDocumentRow | null = null;
  deleting = false;
  deleteError: string | null = null;
  /** The last delete, for the live line. */
  statusMessage = '';

  private returnFocus: HTMLElement | null = null;
  private focusHeadingAfterDelete = false;
  private deleteSub: Subscription | null = null;
  private destroyed = false;

  get headingId(): string {
    return `${this.idPrefix}-heading`;
  }

  get otherSetCount(): number {
    return this.view?.otherSets.length ?? 0;
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.deleteSub?.unsubscribe();
    this.deleteSub = null;
  }

  /** A row's id stem: unique within the list. */
  rowId(row: ComparisonDocumentRow): string {
    return `${this.idPrefix}-row-${row.key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  groupHeadingId(group: ComparisonDocumentGroup): string {
    return `${this.idPrefix}-group-${group.key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  setHeadingId(set: ComparisonOtherModelSet): string {
    return `${this.idPrefix}-set-${set.key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  /** `Written`, `Written with warnings`, `Not written`. */
  statusText(row: ComparisonDocumentRow): string {
    return row.document ? documentStatusLabel(row.document.status ?? 'Completed') : 'Not written';
  }

  /** The row's name for assistive technology: `the Executive Summary of GPT-5.6 Luna (max)`. */
  rowName(row: ComparisonDocumentRow): string {
    return row.subjectLabel ? `the ${row.name} of ${row.subjectLabel}` : `the ${row.name}`;
  }

  /** `Write`, or `Rewrite — replaces the current document`. */
  checkLabel(row: ComparisonDocumentRow): string {
    return row.document ? 'Rewrite — replaces the current document' : 'Write';
  }

  /** Duration, cost and charts, each when known. */
  metaParts(row: ComparisonDocumentRow): string[] {
    const doc = row.document;
    if (!doc) {
      return [];
    }
    const parts: string[] = [];
    if (typeof doc.durationMs === 'number' && doc.durationMs > 0) {
      parts.push(formatElapsed(doc.durationMs));
    }
    if (doc.costUsd !== undefined) {
      parts.push(formatCostUsd(doc.costUsd));
    }
    const charts = chartsPhrase(row.listItem);
    if (charts) {
      parts.push(charts);
    }
    return parts;
  }

  onCheck(row: ComparisonDocumentRow, event: Event): void {
    const input = event.target as HTMLInputElement;
    if (this.busy || !row.checkable) {
      input.checked = row.checked;
      return;
    }
    this.checkChange.emit({ key: row.key, checked: input.checked });
  }

  chooseSet(set: ComparisonOtherModelSet): void {
    if (set.chooseRefusal) {
      return;
    }
    this.chooseModels.emit(set.entryKeys);
  }

  // -------------------------------------------------------------------------------------------
  // View
  // -------------------------------------------------------------------------------------------

  /**
   * Opens a document in the PDF viewer at the fullest disclosure it allows, the others offered as
   * versions; with models besides the subject, their naming is a second choice that opens at *Named*.
   */
  viewDocument(row: ComparisonDocumentRow, button: HTMLElement): void {
    const doc = row.document;
    if (!doc) {
      return;
    }
    this.returnFocus = button;
    const item = row.listItem;
    const allowed = [...new Set(item?.allowedDisclosures ?? [])].sort((a, b) => a - b);
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
    const comparisonScope = item?.scope === BenchmarkReportScope.Comparison || this.view?.mode === 'comparison';
    const hasNames = comparisonScope || (item?.peerCount ?? 0) > 0;
    const paper = rememberedPdfPaper();
    const title = item?.title || `${row.name} — ${row.subjectLabel}`;
    this.pdfViewer?.open({
      title,
      subtitle: `${writerPhrase(doc)} on ${formatUtc(doc.createdAtUtc) || 'an unknown date'}`,
      variants: disclosures.map(disclosure => ({ key: reportDisclosureParam(disclosure), label: disclosureLabel(disclosure) })),
      initialVariant: reportDisclosureParam(highest),
      variantsInfo: reportDisclosureInfo(row.audience, { peerNaming: hasNames }),
      ...(hasNames ? {
        secondaryVariants: {
          label: comparisonScope ? 'Model names' : 'Peer names',
          options: [BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized].map(naming => ({
            key: reportPeerNamingParam(naming),
            label: naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized'
          })),
          initial: reportPeerNamingParam(BenchmarkReportPeerNaming.Named)
        }
      } : {}),
      load: (variant, secondary) =>
        this.benchmarkService.getReportDocumentPdf(doc.documentId, disclosureOf(variant), namingOf(secondary), paper),
      tabUrl: (variant, secondary) =>
        this.benchmarkService.reportDocumentPdfUrl(doc.documentId, disclosureOf(variant), namingOf(secondary), paper, true),
      fallbackFileName: item ? `${reportDocumentFileStem(item, row.name)}.pdf` : `report-document-${doc.documentId}.pdf`
    });
  }

  /** The viewer closed: focus returns to the View button that opened it. */
  onViewerClosed(): void {
    const target = this.returnFocus;
    this.returnFocus = null;
    if (target?.isConnected) {
      target.focus();
    }
  }

  /** A nested dialog's close or cancel event, stopped so it never reaches the wizard's dialog. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  // -------------------------------------------------------------------------------------------
  // Delete
  // -------------------------------------------------------------------------------------------

  requestDelete(row: ComparisonDocumentRow, button: HTMLElement): void {
    if (!row.document || this.busy) {
      return;
    }
    this.deleteTarget = row;
    this.deleteError = null;
    this.deleting = false;
    this.returnFocus = button;
    this.focusHeadingAfterDelete = false;
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
    const row = this.deleteTarget;
    const doc = row?.document;
    if (!row || !doc || this.deleting) {
      return;
    }
    this.deleting = true;
    this.deleteError = null;
    this.deleteSub?.unsubscribe();
    this.deleteSub = this.benchmarkService.deleteReportDocument(doc.documentId).subscribe({
      next: () => {
        if (this.destroyed) {
          return;
        }
        this.deleting = false;
        this.statusMessage = `Deleted ${this.rowName(row)}.`;
        this.focusHeadingAfterDelete = true;
        this.deleteDialog?.nativeElement?.close();
        this.documentDeleted.emit(doc.documentId);
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed) {
          return;
        }
        this.deleting = false;
        this.deleteError = serverMessage(error) ?? 'The document could not be deleted.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  /** After a delete, focus goes to the list's heading, the row's buttons being gone; otherwise back to Delete. */
  onDeleteDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.deleteDialog?.nativeElement?.open) {
      return;
    }
    this.deleteTarget = null;
    this.deleteError = null;
    const target = this.focusHeadingAfterDelete
      ? this.host.nativeElement.querySelector<HTMLElement>(`[id="${this.headingId}"]`)
      : this.returnFocus;
    this.focusHeadingAfterDelete = false;
    this.returnFocus = null;
    this.cdr.markForCheck();
    if (target?.isConnected) {
      target.focus();
    }
  }
}
