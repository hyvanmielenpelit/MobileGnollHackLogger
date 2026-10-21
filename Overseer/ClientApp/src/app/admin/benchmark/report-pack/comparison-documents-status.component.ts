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
  BenchmarkReportPackDocumentsDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackWrittenDocumentDto,
  BenchmarkReportPeerNaming,
  BenchmarkReportScope,
  reportDisclosureParam,
  reportPeerNamingParam
} from '../../../services/admin-benchmark.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { LoadingIndicatorComponent } from '../../../shared/loading-indicator/loading-indicator.component';
import { PdfViewerDialogComponent } from '../../../shared/pdf-viewer/pdf-viewer-dialog.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { rememberedPdfPaper, reportDocumentFileStem } from '../download-center/download-center-panel.component';
import { reportDisclosureInfo } from '../report-disclosure-guide';
import { EXECUTIVE_SUMMARY_BLOCKED_ERROR, reportWriteOrderHint } from '../run-ai-reports/report-documents-list';
import {
  MAX_COMPARISON_DOCUMENT_ENTRIES,
  MIN_COMPARISON_DOCUMENT_ENTRIES
} from './comparison-model-options';
import {
  audienceLabel,
  disclosureLabel,
  documentStatusLabel,
  formatCostUsd,
  formatElapsed,
  formatUtc,
  reportAudiencesFor
} from './report-document-format';
import { ReportValidationDetailsComponent } from './report-validation-details.component';
import { DocumentStatusInput, documentStatusQuestion, documentStatusSubject } from './report-validation-explanation';
import { RootCauseRunsComponent } from './root-cause-runs.component';

/** A stored document's validation outcome, read when its explanation is first opened. */
export interface StoredValidationState {
  readonly state: 'loading' | 'ready' | 'error';
  readonly document: DocumentStatusInput | null;
}

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
  /** The next job writes the row: the admin's choice, and for a blocked Executive Summary never. */
  readonly checked: boolean;
  /**
   * Why an Executive Summary row cannot be checked: its group's Root Cause Analysis is neither checked
   * nor written from the data the models have now. Null when it can, and on every other row.
   */
  readonly blockedReason: string | null;
  /** What the inline error's fix checks on the Root Cause Analysis row; null without a fix in the list. */
  readonly blockedFix: 'checkRootCause' | 'checkRootCauseRewrite' | null;
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
  /** `loading` until the stored documents or the preview have answered for the current mode and models. */
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
  /**
   * The stored documents listed for the current mode and models, with nothing prepared; null while they
   * are awaited. Its lists come before the preview's.
   */
  readonly documents: BenchmarkReportPackDocumentsDto | null;
  /** Why the preview failed, or null; not shown once `documents` is in. */
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
    checked: checkable && (input.checks.get(key) ?? defaultRowChecked(document !== null)),
    blockedReason: null,
    blockedFix: null
  };
}

/** The reason line of an Executive Summary row whose stored Root Cause Analysis is being checked. */
export const COMPARISON_SUMMARY_CHECKING_REASON = 'Checking the stored Root Cause Analysis…';

/**
 * Whether an Executive Summary may be written from a written Root Cause Analysis, from the preview:
 * `pending` while the preview is awaited, `failed` when it failed, `unknown` when it did not say (the
 * server's start decides then).
 */
function writtenRootCauseReadiness(
  doc: BenchmarkReportPackWrittenDocumentDto,
  input: ComparisonDocumentsViewInput
): 'usable' | 'outdated' | 'pending' | 'failed' | 'unknown' {
  const preview = input.preview;
  if (!preview) {
    return input.error ? 'failed' : 'pending';
  }
  const answered = [...(preview.writtenDocuments ?? []), ...(preview.subjectDocuments ?? []).flatMap(subject => subject.documents ?? [])]
    .find(candidate => candidate.documentId === doc.documentId);
  return answered?.summaryReadiness ?? doc.summaryReadiness ?? 'unknown';
}

/**
 * Blocks a group's Executive Summary row when its Root Cause Analysis row is not checked and the Root
 * Cause Analysis is not written from the data the models have now: the summary is written only from
 * one. A blocked row reads unchecked; the admin's own choice stays in `checks` and returns with it.
 */
function withRootCauseDependency(
  rows: ComparisonDocumentRow[],
  input: ComparisonDocumentsViewInput
): ComparisonDocumentRow[] {
  const rootCause = rows.find(row => row.audience === BenchmarkReportAudience.RootCauseAnalysis);
  if (!rootCause || rootCause.checked) {
    return rows;
  }
  const owner = input.mode === 'comparison' ? 'this comparison' : 'this model';
  let reason: string | null = null;
  let fix: ComparisonDocumentRow['blockedFix'] = null;
  const doc = rootCause.document;
  if (!doc) {
    reason = `Needs ${owner}'s Root Cause Analysis: check Write on the Root Cause Analysis above.`;
    fix = 'checkRootCause';
  } else {
    switch (writtenRootCauseReadiness(doc, input)) {
      case 'outdated':
        reason = `Root Cause Analysis #${doc.documentId} was written from different data. Check Rewrite on it to write both.`;
        fix = 'checkRootCauseRewrite';
        break;
      case 'pending':
        reason = COMPARISON_SUMMARY_CHECKING_REASON;
        break;
      case 'failed':
        reason = 'The stored Root Cause Analysis could not be checked.';
        break;
      default:
        return rows;
    }
  }
  return rows.map(row => row.audience === BenchmarkReportAudience.ExecutiveSummary && row.checkable
    ? { ...row, checked: false, blockedReason: reason, blockedFix: fix }
    : row);
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
 * per document type. Rows are in write order, the Root Cause Analysis first, and a group's Executive
 * Summary row depends on its Root Cause Analysis row. The documents come from `documents` when it is
 * in, else from the preview; a written Root Cause Analysis's readiness comes from the preview.
 */
export function comparisonDocumentsView(input: ComparisonDocumentsViewInput): ComparisonDocumentsView {
  const preview = input.preview;
  const stored = input.documents;
  if (!preview && !stored) {
    return {
      mode: input.mode,
      state: input.error ? 'error' : 'loading',
      message: input.error,
      groups: [],
      otherSets: []
    };
  }
  const audiences = reportAudiencesFor(input.mode).map(option => option.audience);
  if (input.mode === 'comparison') {
    const written = (stored ? stored.writtenDocuments : preview?.writtenDocuments) ?? [];
    // The entries that are not Excluded, the same count the preview gives.
    const entryCount = preview ? preview.comparisonEntryCount : input.offeredKeys.length;
    const label = input.chosenKeys.map(key => input.labelOf(key)).join(', ');
    const rows = withRootCauseDependency(audiences.map(audience => rowOf(
      `comparison|${audience}`, audience, label, null, written.find(doc => doc.audience === audience) ?? null, input, true)), input);
    const offered = new Set(input.offeredKeys);
    const otherSets = ((stored ? stored.otherModelSets : preview?.otherModelSets) ?? [])
      .filter(set => (set.documents ?? []).length > 0)
      .map((set): ComparisonOtherModelSet => {
        const models = set.coveredModels ?? [];
        const keys = models.map(model => model.entryKey);
        const setLabel = `${models.map(model => model.label).join(', ')} — `
          + setSizeText(models.length, entryCount, set.coversAllEntries);
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

  const subjects = (stored ? stored.subjectDocuments : preview?.subjectDocuments) ?? [];
  const groups = input.chosenKeys.map((entryKey): ComparisonDocumentGroup => {
    const documents = subjects.find(subject => subject.subjectKey === entryKey)?.documents ?? [];
    const heading = input.labelOf(entryKey);
    return {
      key: entryKey,
      heading,
      rows: withRootCauseDependency(audiences.map(audience => rowOf(
        `${entryKey}|${audience}`, audience, heading, entryKey, documents.find(doc => doc.audience === audience) ?? null, input, true)), input)
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
 * the next job, and icon-only View (the PDF viewer) and Delete (after a confirmation). A written Root
 * Cause Analysis adds an icon-only Redraw charts and, under its row, its next runs and checks. A
 * document written with warnings has an (i) button beside its status whose dialog says what was removed
 * and what was kept, read from the stored document when it is first opened. In
 * comparison scope, the other covered sets with documents follow in a closed disclosure, each with
 * **Choose these models**. A blocked Executive Summary row's checkbox is `aria-disabled` and described
 * by its reason line; a click on it shows an inline error with a button that checks the Root Cause
 * Analysis and the summary together.
 *
 * The host owns the rows and the checks; this component owns the viewer, the delete and the redraw.
 */
@Component({
  selector: 'app-comparison-documents-status',
  standalone: true,
  imports: [
    NgTemplateOutlet, PdfViewerDialogComponent, RootCauseRunsComponent, InfoTipComponent, ReportValidationDetailsComponent,
    LoadingIndicatorComponent
  ],
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
  readonly rootCauseAudience = BenchmarkReportAudience.RootCauseAnalysis;

  deleteTarget: ComparisonDocumentRow | null = null;
  deleting = false;
  deleteError: string | null = null;
  /** The last delete or redraw, for the live line. */
  statusMessage = '';

  /** The document whose charts are being redrawn. */
  redrawingDocumentId: number | null = null;
  /** Why the last redraw failed. */
  redrawError: string | null = null;

  /** Each stored document's validation outcome, by document id, once its explanation has been opened. */
  readonly validation = new Map<number, StoredValidationState>();

  private returnFocus: HTMLElement | null = null;
  private focusHeadingAfterDelete = false;
  private deleteSub: Subscription | null = null;
  private redrawSub: Subscription | null = null;
  private readonly validationSubs = new Map<number, Subscription>();
  private destroyed = false;

  get headingId(): string {
    return `${this.idPrefix}-heading`;
  }

  get otherSetCount(): number {
    return this.view?.otherSets.length ?? 0;
  }

  /**
   * The write order of the checked rows, under the lists: one line, at most once, while a group has
   * both documents checked. An Executive Summary row says on its own what it is written from.
   */
  get writeOrderHints(): string[] {
    const view = this.view;
    if (!view || view.state !== 'ready') {
      return [];
    }
    const hints = view.groups
      .map(group => reportWriteOrderHint(group.rows.filter(row => row.checked).map(row => row.audience)))
      .filter(hint => hint !== '');
    return [...new Set(hints)];
  }

  /** The blocked Executive Summary row whose click was refused; its inline error shows while it stays blocked. */
  refusedRowKey: string | null = null;

  /** The inline error under a blocked Executive Summary row that was clicked. */
  readonly summaryBlockedError = EXECUTIVE_SUMMARY_BLOCKED_ERROR;

  /** The row's inline error shows: its click was refused, and it is still blocked. */
  showsBlockedError(row: ComparisonDocumentRow): boolean {
    return this.refusedRowKey === row.key && row.blockedReason !== null;
  }

  /** A checkbox's description: a blocked row's reason line, then its inline error while it shows. */
  checkDescribedBy(row: ComparisonDocumentRow): string | null {
    if (row.blockedReason === null) {
      return null;
    }
    const rid = this.rowId(row);
    return this.showsBlockedError(row) ? `${rid}-reason ${rid}-error` : `${rid}-reason`;
  }

  /** The fix button's label: what it checks on the Root Cause Analysis row. */
  fixLabel(row: ComparisonDocumentRow): string {
    return row.blockedFix === 'checkRootCauseRewrite'
      ? 'Check Rewrite on the Root Cause Analysis'
      : 'Check the Root Cause Analysis';
  }

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.deleteSub?.unsubscribe();
    this.deleteSub = null;
    this.redrawSub?.unsubscribe();
    this.redrawSub = null;
    this.validationSubs.forEach(sub => sub.unsubscribe());
    this.validationSubs.clear();
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
    if (this.busy || !row.checkable || row.blockedReason !== null) {
      input.checked = row.checked;
      return;
    }
    this.refusedRowKey = null;
    this.checkChange.emit({ key: row.key, checked: input.checked });
  }

  /**
   * A click on a blocked Executive Summary row is refused: the checkbox stays unchecked and the inline
   * error says why. A Space press arrives as a click too.
   */
  onCheckClick(row: ComparisonDocumentRow, event: Event): void {
    if (row.blockedReason === null || this.busy) {
      return;
    }
    event.preventDefault();
    (event.target as HTMLInputElement).checked = false;
    this.refusedRowKey = row.key;
    this.cdr.markForCheck();
  }

  /**
   * The inline error's fix: checks the Root Cause Analysis row of the Executive Summary's group (Write or
   * Rewrite), then the Executive Summary row, which takes focus.
   */
  applyFix(row: ComparisonDocumentRow): void {
    if (this.busy || row.blockedFix === null) {
      return;
    }
    const group = this.view?.groups.find(candidate => candidate.rows.some(other => other.key === row.key));
    const rootCause = group?.rows.find(other => other.audience === BenchmarkReportAudience.RootCauseAnalysis);
    if (!rootCause) {
      return;
    }
    this.refusedRowKey = null;
    this.checkChange.emit({ key: rootCause.key, checked: true });
    this.checkChange.emit({ key: row.key, checked: true });
    this.cdr.markForCheck();
    this.host.nativeElement.querySelector<HTMLInputElement>(`[id="${this.rowId(row)}-check"]`)?.focus();
  }

  chooseSet(set: ComparisonOtherModelSet): void {
    if (set.chooseRefusal) {
      return;
    }
    this.chooseModels.emit(set.entryKeys);
  }

  // -------------------------------------------------------------------------------------------
  // Why a document was written with warnings
  // -------------------------------------------------------------------------------------------

  /** `warnings in the Executive Summary of …`: the explanation button's subject. */
  whySubject(row: ComparisonDocumentRow): string {
    return documentStatusSubject(this.rowName(row), row.document?.status ?? 'CompletedWithWarnings');
  }

  /** `Why the Executive Summary of … was completed with warnings`: the explanation dialog's title. */
  whyTitle(row: ComparisonDocumentRow): string {
    return documentStatusQuestion(this.rowName(row), row.document?.status ?? 'CompletedWithWarnings');
  }

  /**
   * Reads the stored document's validation issues the first time its explanation is opened, and again
   * after a failed read. A server that sends no `validationIssues` gives its notes without rule names.
   */
  loadValidation(row: ComparisonDocumentRow): void {
    const doc = row.document;
    if (!doc) {
      return;
    }
    const id = doc.documentId;
    const current = this.validation.get(id);
    if (current && current.state !== 'error') {
      return;
    }
    const status = doc.status ?? 'CompletedWithWarnings';
    this.validation.set(id, { state: 'loading', document: null });
    this.validationSubs.get(id)?.unsubscribe();
    this.validationSubs.set(id, this.benchmarkService.getReportDocument(id).subscribe({
      next: detail => {
        if (this.destroyed) {
          return;
        }
        this.validation.set(id, {
          state: 'ready',
          document: { status, finalIssues: detail?.validationIssues ?? detail?.validationNotes ?? [] }
        });
        this.cdr.markForCheck();
      },
      error: () => {
        if (this.destroyed) {
          return;
        }
        this.validation.set(id, { state: 'error', document: null });
        this.cdr.markForCheck();
      }
    }));
    this.cdr.markForCheck();
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
  // Redraw
  // -------------------------------------------------------------------------------------------

  /** Draws a written Root Cause Analysis' server-drawn charts again from its stored facts. */
  redrawCharts(row: ComparisonDocumentRow): void {
    const doc = row.document;
    if (!doc || this.busy || this.redrawingDocumentId !== null) {
      return;
    }
    this.redrawingDocumentId = doc.documentId;
    this.redrawError = null;
    this.statusMessage = '';
    this.redrawSub?.unsubscribe();
    this.redrawSub = this.benchmarkService.redrawReportDocumentCharts(doc.documentId).subscribe({
      next: summary => {
        if (this.destroyed) {
          return;
        }
        this.redrawingDocumentId = null;
        const count = summary?.chartCount ?? 0;
        this.statusMessage = `Redrew the charts of ${this.rowName(row)} (${count} ${count === 1 ? 'image' : 'images'}).`;
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed) {
          return;
        }
        this.redrawingDocumentId = null;
        this.redrawError = serverMessage(error) ?? `The charts of ${this.rowName(row)} could not be redrawn.`;
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
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
