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
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import {
  AdminBenchmarkService,
  BenchmarkPdfPaper,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPeerNaming,
  reportDisclosureParam,
  reportPeerNamingParam
} from '../../../services/admin-benchmark.service';
import { downloadTextFile, safeFileName } from '../../../utils/download.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { exportTimestamp, saveFigureBlob } from '../model-comparison/figure-export';
import { markdownToPrintableHtml } from './printable-html';
import {
  ArchiveEntry,
  MANIFEST_FILE_NAME,
  ManifestFailure,
  ManifestFile,
  buildManifest,
  buildTextArchive,
  paperLabel,
  uniqueFileNames
} from './text-archive';

/** The run a run-context Download Center packages files of. */
export interface DownloadCenterRunInfo {
  id: number;
  suiteName: string;
  modelLabel: string;
  startedAtUtc: string;
  completedAtUtc: string | null;
}

/** Opened from a run report: the run's files and every pack document whose subject includes the run. */
export interface DownloadCenterRunContext {
  kind: 'run';
  run: DownloadCenterRunInfo;
  /** The run diagnostics text, captured when the download is prepared. */
  diagnosticsText: () => string;
}

/** Opened from the Report Pack dialog: the chosen documents and the reports of their subjects' runs. */
export interface DownloadCenterDocumentsContext {
  kind: 'documents';
  documentIds: number[];
}

export type DownloadCenterContext = DownloadCenterRunContext | DownloadCenterDocumentsContext;

export type DownloadPackageId = 'internal' | 'provider' | 'custom';
export type DownloadFormat = 'pdf' | 'docx' | 'md' | 'html' | 'txt';
export type DownloadRowKind = 'pack' | 'runReport' | 'toolCallLog' | 'diagnostics';

/** What a remembered choice is keyed by: a pack document's audience, or a run file's kind. */
export type DownloadRowCategory =
  | 'executiveSummary'
  | 'technicalReport'
  | 'internalBrief'
  | 'runReport'
  | 'toolCallLog'
  | 'diagnostics';

/** One available document: a row of the Documents table. */
export interface DownloadRow {
  key: string;
  kind: DownloadRowKind;
  category: DownloadRowCategory;
  label: string;
  /** What identifies the row: its subject, audience, writer and time. */
  detail: string;
  /** An explanation shown behind the row's info button, or null. */
  note: string | null;
  runId: number | null;
  doc: BenchmarkReportDocumentListItemDto | null;
  /** A subject run was re-scored, re-run or deleted since the document was written. */
  runChanged: boolean;
  /** The disclosures a pack document renders at; empty for run files. */
  allowedDisclosures: BenchmarkReportDisclosure[];
  /** The formats this row can be downloaded in. */
  formats: readonly DownloadFormat[];
  /** Why the row is internal only whatever is chosen, or null for a shareable pack document. */
  internalReason: string | null;
}

export interface DownloadRowState {
  selected: boolean;
  disclosure: BenchmarkReportDisclosure;
  naming: BenchmarkReportPeerNaming;
  formats: DownloadFormat[];
}

export interface DownloadPackage {
  id: DownloadPackageId;
  name: string;
  /** The name in the summary line, the manifest and the ZIP file name. */
  fullName: string;
  /** One line under the name on the package card. */
  tagline: string;
  description: string;
}

/** A failure the dialog lists after a download. */
export interface DownloadFailure {
  label: string;
  reason: string;
}

/** A download in preparation: finished steps of `total` (every file, then the ZIP or the save) and the current one. */
export interface DownloadProgress {
  done: number;
  total: number;
  step: string;
}

/** The text a row's text formats are made from; `capturedAt` is set for the diagnostics. */
interface SourceText {
  text: string;
  fileName: string | null;
  capturedAt: Date | null;
}

/** A PDF or Word document the server rendered, with the name it gave it. */
interface SourceBinary {
  bytes: Uint8Array;
  fileName: string | null;
  capturedAt: Date | null;
}

/** A finished file: a PDF's or Word document's bytes, or the text of every other format. */
type ProducedFile = { name: string; mime: string; mtime: Date; manifest: ManifestFile }
  & ({ text: string } | { bytes: Uint8Array });

/** Where finished files go. A holder, so a spec can stand fakes in for the browser download path. */
export const downloadCenterIo = {
  saveText: (fileName: string, text: string, mimeType: string): void => downloadTextFile(fileName, text, mimeType),
  saveBytes: (fileName: string, bytes: Uint8Array, mimeType: string): void =>
    saveFigureBlob(new Blob([bytes as unknown as BlobPart], { type: mimeType }), fileName),
  saveBlob: (blob: Blob, fileName: string): void => saveFigureBlob(blob, fileName),
  now: (): Date => new Date()
};

export const DOWNLOAD_CENTER_STORAGE_KEY = 'overseer.benchmark.downloadCenter';

/**
 * The stored settings' version. Version 2 is migrated on reading, without the Internal package's
 * remembered formats; settings of any other version read as absent.
 */
export const STORED_SETTINGS_VERSION = 3;

/** One remembered row choice; every field is checked against the row before it is applied. */
interface StoredChoice {
  selected?: unknown;
  disclosure?: unknown;
  naming?: unknown;
  formats?: unknown;
}

interface StoredSettings {
  version: typeof STORED_SETTINGS_VERSION;
  package?: DownloadPackageId;
  paper?: BenchmarkPdfPaper;
  packages?:Partial<Record<DownloadPackageId, Partial<Record<DownloadRowCategory, StoredChoice>>>>;
}

export const DOWNLOAD_PACKAGES: readonly DownloadPackage[] = [
  {
    id: 'internal',
    name: 'Internal',
    fullName: 'Internal package',
    tagline: 'Every file, for the Overseer team',
    description: 'Every available file for the Overseer team, as PDF, Word and Markdown (PDF, Word and Text for the diagnostics): report documents at Full disclosure with peers named, the run report, the tool-call log and the diagnostics.'
  },
  {
    id: 'provider',
    name: 'External',
    fullName: 'External package',
    tagline: 'Shareable reports for a model’s provider',
    description: 'The Executive Summary and the Report for AI Researchers and Developers as PDF, to send to a model’s provider: Summary disclosure (Detailed optional), peers anonymized (named optional). Internal-only files cannot be chosen.'
  },
  {
    id: 'custom',
    name: 'Custom',
    fullName: 'Custom',
    tagline: 'Your own selection',
    description: 'Any selection, at any level a document allows.'
  }
];

export const INTERNAL_REASONS = {
  internalBrief: 'Internal only: contains rubric text',
  fullDisclosure: 'Internal only at Full: contains rubric text',
  runReport: 'Internal only: contains questions, rubrics and answers',
  toolCallLog: 'Internal only: contains every tool call’s arguments and results',
  diagnostics: 'Internal only: contains the run’s configuration and internal log'
} as const;

const MIME_TYPES: Record<DownloadFormat, string> = {
  pdf: 'application/pdf',
  docx: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  md: 'text/markdown;charset=utf-8',
  html: 'text/html;charset=utf-8',
  txt: 'text/plain;charset=utf-8'
};

const FORMAT_LABELS: Record<DownloadFormat, string> = { pdf: 'PDF', docx: 'Word', md: 'Markdown', html: 'HTML', txt: 'Text' };

/**
 * The formats a package chooses where a row offers them: External the PDF alone; Internal, and Custom
 * with nothing to keep, the PDF, the Word document and the text they are made from (Markdown, or Text
 * for the diagnostics).
 */
const PRESET_FORMATS: Record<'internal' | 'provider', readonly DownloadFormat[]> = {
  internal: ['pdf', 'docx', 'md', 'txt'],
  provider: ['pdf']
};

export const PDF_PAPERS: readonly { id: BenchmarkPdfPaper; label: string }[] = [
  { id: 'a4', label: paperLabel('a4') },
  { id: 'letter', label: paperLabel('letter') }
];

const ALL_DISCLOSURES = [BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full];

let nextInstanceId = 0;

/**
 * The Download Center: packages report documents and run files for download, as one file or as a
 * ZIP with `MANIFEST.md`. Three packages set the choices (Internal, External, Custom); the table
 * lists every available document with its options.
 *
 * It makes no request but the document list or detail that fills the table, the render endpoints
 * (Markdown, PDF and Word), the run report and tool-call log endpoints with their PDFs and Word
 * documents, and the diagnostics PDF and Word endpoints, which render the captured text and store
 * nothing: nothing here can start generation.
 */
@Component({
  selector: 'app-benchmark-download-center',
  standalone: true,
  imports: [InfoTipComponent],
  templateUrl: './benchmark-download-center.component.html',
  styleUrls: ['./benchmark-download-center.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkDownloadCenterComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('downloadCenterDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('downloadCenterHeading') heading?: ElementRef<HTMLElement>;

  /** Emitted when the dialog closes, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();

  readonly idPrefix = `dc${++nextInstanceId}`;
  readonly packages = DOWNLOAD_PACKAGES;
  readonly papers = PDF_PAPERS;

  context: DownloadCenterContext | null = null;
  packageId: DownloadPackageId = 'internal';
  /** The paper every PDF and Word document of a download is laid out on. */
  paper: BenchmarkPdfPaper = 'a4';
  rows: DownloadRow[] = [];
  loadingDocuments = false;
  /** Notices above the table: runs that no longer exist, documents that could not be loaded. */
  notices: string[] = [];
  preparing = false;
  /** The download in preparation, shown over the dialog body; null while none is. */
  progress: DownloadProgress | null = null;
  statusMessage = '';
  failures: DownloadFailure[] = [];

  private readonly states = new Map<string, DownloadRowState>();
  /** Bumped on every open and close, so a closed dialog's download never lands. */
  private generation = 0;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.generation++;
  }

  // -------------------------------------------------------------------------------------------
  // Opening and closing
  // -------------------------------------------------------------------------------------------

  /** Shows the dialog for a run or a set of pack documents, at the last package and paper used. */
  open(context: DownloadCenterContext): void {
    this.generation++;
    this.context = context;
    this.rows = [];
    this.states.clear();
    this.notices = [];
    this.failures = [];
    this.statusMessage = '';
    this.preparing = false;
    this.progress = null;
    const stored = readStoredSettings();
    this.packageId = stored?.package ?? 'internal';
    this.paper = stored?.paper ?? 'a4';

    if (context.kind === 'run') {
      this.addRows(runFileRows(context.run));
      this.loadRunDocuments(context.run.id, this.generation);
    } else {
      this.loadChosenDocuments(context.documentIds, this.generation);
    }

    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    // showModal() focuses the first focusable element, the Close button, whose hint tooltip opens on focus.
    this.heading?.nativeElement.focus();
    this.cdr.markForCheck();
  }

  close(): void {
    this.dialog?.nativeElement?.close();
  }

  /** The native close event: Escape, Cancel, the close button, or close() from the host. */
  onDialogClose(): void {
    // The close event is queued: one that arrives after a reopen belongs to the earlier opening.
    if (this.dialog?.nativeElement?.open) {
      return;
    }
    this.generation++;
    this.preparing = false;
    this.progress = null;
    this.cdr.markForCheck();
    this.closed.emit();
  }

  get subtitle(): string {
    const context = this.context;
    if (!context) {
      return '';
    }
    if (context.kind === 'run') {
      return `Run #${context.run.id} · ${context.run.suiteName} · ${context.run.modelLabel}`;
    }
    const count = context.documentIds.length;
    return `${count} report document${count === 1 ? '' : 's'} and the reports of their runs`;
  }

  // -------------------------------------------------------------------------------------------
  // Packages and row choices
  // -------------------------------------------------------------------------------------------

  selectPackage(id: DownloadPackageId): void {
    if (this.preparing) {
      return;
    }
    this.packageId = id;
    const stored = readStoredSettings();
    for (const row of this.rows) {
      const preset = this.presetState(row, id);
      this.states.set(row.key, this.rememberedState(row, id, preset, stored));
    }
    this.failures = [];
    this.statusMessage = '';
    this.cdr.markForCheck();
  }

  selectPaper(paper: BenchmarkPdfPaper): void {
    if (this.preparing || !PDF_PAPERS.some(p => p.id === paper)) {
      return;
    }
    this.paper = paper;
    this.cdr.markForCheck();
  }

  get currentPackage(): DownloadPackage {
    return DOWNLOAD_PACKAGES.find(p => p.id === this.packageId) ?? DOWNLOAD_PACKAGES[0];
  }

  /** Internal locks every pack document at Full with peers named. */
  get optionsLocked(): boolean {
    return this.packageId === 'internal';
  }

  stateOf(row: DownloadRow): DownloadRowState {
    let state = this.states.get(row.key);
    if (!state) {
      state = this.presetState(row, this.packageId);
      this.states.set(row.key, state);
    }
    return state;
  }

  /** In the External package, internal-only rows are listed but cannot be chosen. */
  isSelectable(row: DownloadRow): boolean {
    return this.isSelectableIn(row, this.packageId);
  }

  isIncluded(row: DownloadRow): boolean {
    return this.isSelectable(row) && this.stateOf(row).selected;
  }

  /** Internal only whatever is chosen, or at the chosen Full disclosure. */
  isInternalOnly(row: DownloadRow): boolean {
    return row.internalReason !== null
      || (row.kind === 'pack' && this.stateOf(row).disclosure === BenchmarkReportDisclosure.Full);
  }

  internalReasonOf(row: DownloadRow): string | null {
    if (row.internalReason) {
      return row.internalReason;
    }
    return this.isInternalOnly(row) ? INTERNAL_REASONS.fullDisclosure : null;
  }

  disclosureOptions(row: DownloadRow): BenchmarkReportDisclosure[] {
    return this.disclosureOptionsFor(row, this.packageId);
  }

  namingOptions(): BenchmarkReportPeerNaming[] {
    return this.namingOptionsFor(this.packageId);
  }

  disclosureLabel(disclosure: BenchmarkReportDisclosure): string {
    return disclosureLabel(disclosure);
  }

  namingLabel(naming: BenchmarkReportPeerNaming): string {
    return naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized';
  }

  formatLabel(format: DownloadFormat): string {
    return FORMAT_LABELS[format];
  }

  hasFormat(row: DownloadRow, format: DownloadFormat): boolean {
    return this.stateOf(row).formats.includes(format);
  }

  rowId(row: DownloadRow): string {
    return `${this.idPrefix}-${row.key.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  }

  /** The row checkbox's description: why it cannot be chosen, and the row's note. */
  rowDescribedBy(row: DownloadRow): string | null {
    const rid = this.rowId(row);
    const ids: string[] = [];
    if (!this.isSelectable(row)) {
      ids.push(`${rid}-reason`);
    }
    if (row.note) {
      ids.push(`${rid}-note`);
    }
    return ids.length > 0 ? ids.join(' ') : null;
  }

  toggleRow(row: DownloadRow, event: Event): void {
    if (!this.isSelectable(row) || this.preparing) {
      return;
    }
    this.stateOf(row).selected = (event.target as HTMLInputElement).checked;
    this.cdr.markForCheck();
  }

  setDisclosure(row: DownloadRow, event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value) as BenchmarkReportDisclosure;
    if (this.optionsLocked || !this.disclosureOptions(row).includes(value)) {
      return;
    }
    this.stateOf(row).disclosure = value;
    this.cdr.markForCheck();
  }

  setNaming(row: DownloadRow, event: Event): void {
    const value = Number((event.target as HTMLSelectElement).value) as BenchmarkReportPeerNaming;
    if (this.optionsLocked || !this.namingOptions().includes(value)) {
      return;
    }
    this.stateOf(row).naming = value;
    this.cdr.markForCheck();
  }

  toggleFormat(row: DownloadRow, format: DownloadFormat, event: Event): void {
    if (!row.formats.includes(format)) {
      return;
    }
    const state = this.stateOf(row);
    const checked = (event.target as HTMLInputElement).checked;
    const next = new Set(state.formats);
    if (checked) {
      next.add(format);
    } else {
      next.delete(format);
    }
    state.formats = row.formats.filter(f => next.has(f));
    this.cdr.markForCheck();
  }

  /** A shareable pack document chosen with its peers named. */
  get namedPeersWarning(): boolean {
    return this.packageId !== 'internal' && this.rows.some(row =>
      row.kind === 'pack' && this.isIncluded(row) && !this.isInternalOnly(row)
      && this.stateOf(row).naming === BenchmarkReportPeerNaming.Named);
  }

  // -------------------------------------------------------------------------------------------
  // The download
  // -------------------------------------------------------------------------------------------

  /** Every file the current choices produce, in table order. */
  get plannedFiles(): { row: DownloadRow; format: DownloadFormat }[] {
    const files: { row: DownloadRow; format: DownloadFormat }[] = [];
    for (const row of this.rows) {
      if (!this.isIncluded(row)) {
        continue;
      }
      for (const format of row.formats) {
        if (this.stateOf(row).formats.includes(format)) {
          files.push({ row, format });
        }
      }
    }
    return files;
  }

  /** `5 files · 1 ZIP · Internal package`. */
  get summaryLine(): string {
    const count = this.plannedFiles.length;
    const name = this.currentPackage.fullName;
    if (count === 0) {
      return `No files chosen · ${name}`;
    }
    if (count === 1) {
      return `1 file · ${name}`;
    }
    return `${count} files · 1 ZIP · ${name}`;
  }

  get canDownload(): boolean {
    return !this.preparing && !this.loadingDocuments && this.plannedFiles.length > 0;
  }

  /**
   * Fetches and converts every chosen file, one at a time, then saves one file as itself or
   * several as a ZIP with `MANIFEST.md`. A file that fails is listed and the rest still download.
   */
  async download(): Promise<void> {
    if (!this.canDownload || !this.context) {
      return;
    }
    const generation = this.generation;
    const context = this.context;
    const packagedAt = downloadCenterIo.now();
    const plan = this.plannedFiles.map(file => ({ ...file, state: { ...this.stateOf(file.row), formats: [...this.stateOf(file.row).formats] } }));
    const packageName = this.currentPackage.fullName;
    const paper = this.paper;
    const texts = new Map<string, Promise<SourceText>>();

    const progress: DownloadProgress = { done: 0, total: plan.length + 1, step: '' };
    this.preparing = true;
    this.progress = progress;
    this.failures = [];
    this.persistSettings();

    const produced: ProducedFile[] = [];
    const failures: DownloadFailure[] = [];

    for (let i = 0; i < plan.length; i++) {
      const { row, format, state } = plan[i];
      this.showStep(progress, `Preparing ${i + 1} of ${plan.length} — ${row.label} (${FORMAT_LABELS[format]})`);
      try {
        const source = format === 'pdf' || format === 'docx'
          ? await this.binarySource(row, state, context, texts, paper, format)
          : await this.sourceText(row, state, context, texts);
        if (generation !== this.generation) {
          this.releaseProgress(progress);
          return;
        }
        produced.push(this.produceFile(row, state, format, source, context, packagedAt, paper));
      } catch (error) {
        if (generation !== this.generation) {
          this.releaseProgress(progress);
          return;
        }
        failures.push({ label: `${row.label} (${FORMAT_LABELS[format]})`, reason: failureReason(error, row) });
      }
      progress.done++;
    }

    if (produced.length > 0) {
      this.showStep(progress, plan.length === 1 ? 'Saving…' : 'Building the ZIP…');
    }

    try {
      if (plan.length === 1) {
        if (produced.length === 1) {
          const only = produced[0];
          if ('bytes' in only) {
            downloadCenterIo.saveBytes(only.name, only.bytes, only.mime);
          } else {
            downloadCenterIo.saveText(only.name, only.text, only.mime);
          }
        }
      } else if (produced.length > 0) {
        const names = uniqueFileNames([MANIFEST_FILE_NAME, ...produced.map(file => file.name)]).slice(1);
        const manifestFiles = produced.map((file, index) => ({ ...file.manifest, name: names[index] }));
        const manifest = await buildManifest({
          packageName,
          packagedAt,
          files: manifestFiles,
          failures: failures as ManifestFailure[]
        });
        const entries: ArchiveEntry[] = produced.map((file, index): ArchiveEntry => 'bytes' in file
          ? { name: names[index], bytes: file.bytes, mtime: file.mtime }
          : { name: names[index], text: file.text, mtime: file.mtime });
        entries.push({ name: MANIFEST_FILE_NAME, text: manifest, mtime: packagedAt });
        const archive = await buildTextArchive(entries);
        if (generation !== this.generation) {
          this.releaseProgress(progress);
          return;
        }
        downloadCenterIo.saveBlob(archive, this.zipFileName(context, packagedAt));
      }
    } catch (error) {
      if (generation !== this.generation) {
        this.releaseProgress(progress);
        return;
      }
      failures.push({ label: 'The ZIP', reason: error instanceof Error ? error.message : 'it could not be built' });
      produced.length = 0;
    }

    this.preparing = false;
    this.progress = null;
    this.failures = failures;
    this.statusMessage = completionMessage(plan.length, produced.length, failures.length);
    this.cdr.markForCheck();
  }

  /** Shows a download step in the overlay and in the footer's status line. */
  private showStep(progress: DownloadProgress, step: string): void {
    progress.step = step;
    this.statusMessage = step;
    this.cdr.markForCheck();
  }

  /** Clears an abandoned download's progress, unless a newer download has already replaced it. */
  private releaseProgress(progress: DownloadProgress): void {
    if (this.progress === progress) {
      this.progress = null;
      this.cdr.markForCheck();
    }
  }

  // -------------------------------------------------------------------------------------------
  // Internals
  // -------------------------------------------------------------------------------------------

  private loadRunDocuments(runId: number, generation: number): void {
    this.loadingDocuments = true;
    this.benchmarkService.listReportDocuments({ runId }).subscribe({
      next: documents => {
        if (generation !== this.generation) {
          return;
        }
        this.addRows(sortDocuments(documents).map(packRow));
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }
        this.notices = [...this.notices, 'The report documents of this run could not be loaded; the run files are still listed.'];
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      }
    });
  }

  private loadChosenDocuments(ids: readonly number[], generation: number): void {
    const unique = Array.from(new Set(ids));
    if (unique.length === 0) {
      return;
    }
    this.loadingDocuments = true;
    forkJoin(unique.map(id => this.benchmarkService.getReportDocument(id).pipe(catchError(() => of(null)))))
      .subscribe(details => {
        if (generation !== this.generation) {
          return;
        }
        const documents: BenchmarkReportDocumentListItemDto[] = [];
        const notices: string[] = [];
        details.forEach((detail, index) => {
          if (detail) {
            documents.push(detail);
          } else {
            notices.push(`Report document #${unique[index]} is no longer available.`);
          }
        });

        const missing = new Set(documents.flatMap(doc => doc.missingRunIds ?? []));
        const runIds = Array.from(new Set(documents.flatMap(doc => doc.subjectRunIds ?? []))).sort((a, b) => a - b);
        for (const runId of Array.from(missing).sort((a, b) => a - b)) {
          notices.push(`Run #${runId} no longer exists, so its run report is not listed.`);
        }

        const rows = documents.map(packRow);
        for (const runId of runIds) {
          if (!missing.has(runId)) {
            const owner = documents.find(doc => doc.subjectRunIds.includes(runId));
            rows.push(subjectRunReportRow(runId, owner?.subjectLabel ?? ''));
          }
        }
        this.notices = [...this.notices, ...notices];
        this.addRows(rows);
        this.loadingDocuments = false;
        this.cdr.markForCheck();
      });
  }

  private addRows(rows: DownloadRow[]): void {
    const stored = readStoredSettings();
    for (const row of rows) {
      if (this.rows.some(existing => existing.key === row.key)) {
        continue;
      }
      this.rows = [...this.rows, row];
      this.states.set(row.key, this.rememberedState(row, this.packageId, this.presetState(row, this.packageId), stored));
    }
  }

  private isSelectableIn(row: DownloadRow, pkg: DownloadPackageId): boolean {
    if (pkg !== 'provider') {
      return true;
    }
    return row.kind === 'pack' && row.internalReason === null && this.disclosureOptionsFor(row, pkg).length > 0;
  }

  private disclosureOptionsFor(row: DownloadRow, pkg: DownloadPackageId): BenchmarkReportDisclosure[] {
    if (row.kind !== 'pack') {
      return [];
    }
    const allowed = ALL_DISCLOSURES.filter(d => row.allowedDisclosures.includes(d));
    if (pkg === 'internal') {
      return allowed.includes(BenchmarkReportDisclosure.Full) ? [BenchmarkReportDisclosure.Full] : allowed.slice(-1);
    }
    if (pkg === 'provider') {
      return allowed.filter(d => d !== BenchmarkReportDisclosure.Full);
    }
    return allowed;
  }

  private namingOptionsFor(pkg: DownloadPackageId): BenchmarkReportPeerNaming[] {
    return pkg === 'internal'
      ? [BenchmarkReportPeerNaming.Named]
      : [BenchmarkReportPeerNaming.Anonymized, BenchmarkReportPeerNaming.Named];
  }

  /** The package's own choice for a row, before anything remembered is applied. */
  private presetState(row: DownloadRow, pkg: DownloadPackageId): DownloadRowState {
    const options = this.disclosureOptionsFor(row, pkg);
    if (pkg === 'provider') {
      return {
        selected: this.isSelectableIn(row, pkg) && (row.category === 'executiveSummary' || row.category === 'technicalReport'),
        disclosure: options.includes(BenchmarkReportDisclosure.Summary) ? BenchmarkReportDisclosure.Summary : (options[0] ?? BenchmarkReportDisclosure.Summary),
        naming: BenchmarkReportPeerNaming.Anonymized,
        formats: presetFormats(row, 'provider')
      };
    }
    if (pkg === 'custom') {
      const current = this.states.get(row.key);
      if (current) {
        return {
          selected: current.selected,
          disclosure: options.includes(current.disclosure) ? current.disclosure : (options[options.length - 1] ?? current.disclosure),
          naming: current.naming,
          formats: [...current.formats]
        };
      }
    }
    return {
      selected: true,
      disclosure: options[options.length - 1] ?? BenchmarkReportDisclosure.Full,
      naming: BenchmarkReportPeerNaming.Named,
      formats: presetFormats(row, 'internal')
    };
  }

  /** The remembered choice of this package for the row's category, where it is still valid. */
  private rememberedState(
    row: DownloadRow,
    pkg: DownloadPackageId,
    preset: DownloadRowState,
    stored: StoredSettings | null
  ): DownloadRowState {
    const choice = stored?.packages?.[pkg]?.[row.category];
    if (!choice || typeof choice !== 'object') {
      return preset;
    }
    const disclosures = this.disclosureOptionsFor(row, pkg);
    const namings = this.namingOptionsFor(pkg);
    const formats = Array.isArray(choice.formats)
      ? row.formats.filter(format => (choice.formats as unknown[]).includes(format))
      : [];
    return {
      selected: this.isSelectableIn(row, pkg) && (typeof choice.selected === 'boolean' ? choice.selected : preset.selected),
      disclosure: disclosures.includes(choice.disclosure as BenchmarkReportDisclosure)
        ? choice.disclosure as BenchmarkReportDisclosure
        : preset.disclosure,
      naming: namings.includes(choice.naming as BenchmarkReportPeerNaming)
        ? choice.naming as BenchmarkReportPeerNaming
        : preset.naming,
      formats: formats.length > 0 ? formats : preset.formats
    };
  }

  /** Remembers the package and paper used and, per category, the choice of its first row. */
  private persistSettings(): void {
    const choices: Partial<Record<DownloadRowCategory, StoredChoice>> = {};
    for (const row of this.rows) {
      if (choices[row.category] || !this.isSelectable(row)) {
        continue;
      }
      const state = this.stateOf(row);
      choices[row.category] = {
        selected: state.selected,
        disclosure: state.disclosure,
        naming: state.naming,
        formats: [...state.formats]
      };
    }
    const stored = readStoredSettings();
    const settings: StoredSettings = {
      version: STORED_SETTINGS_VERSION,
      package: this.packageId,
      paper: this.paper,
      packages: { ...(stored?.packages ?? {}), [this.packageId]: { ...(stored?.packages?.[this.packageId] ?? {}), ...choices } }
    };
    try {
      localStorage.setItem(DOWNLOAD_CENTER_STORAGE_KEY, JSON.stringify(settings));
    } catch {
      // Storage full or unavailable: the choices are simply not remembered.
    }
  }

  /**
   * The text a row's files are made from, fetched once however many formats use it: the diagnostics
   * are captured once per download, and the same capture feeds their Text, their PDF and their Word
   * document.
   */
  private sourceText(
    row: DownloadRow,
    state: DownloadRowState,
    context: DownloadCenterContext,
    cache: Map<string, Promise<SourceText>>
  ): Promise<SourceText> {
    const key = row.kind === 'pack'
      ? `render:${row.doc!.id}:${state.disclosure}:${state.naming}`
      : `${row.kind}:${row.runId}`;
    let pending = cache.get(key);
    if (!pending) {
      pending = this.fetchText(row, state, context);
      cache.set(key, pending);
    }
    return pending;
  }

  private async fetchText(
    row: DownloadRow,
    state: DownloadRowState,
    context: DownloadCenterContext
  ): Promise<SourceText> {
    switch (row.kind) {
      case 'pack':
        return {
          text: await firstValueFrom(this.benchmarkService.renderReportDocument(row.doc!.id, state.disclosure, state.naming)),
          fileName: null,
          capturedAt: null
        };
      case 'runReport':
        return { ...await firstValueFrom(this.benchmarkService.getRunReportText(row.runId!)), capturedAt: null };
      case 'toolCallLog':
        return { ...await firstValueFrom(this.benchmarkService.getToolCallLogText(row.runId!)), capturedAt: null };
      case 'diagnostics':
        if (context.kind !== 'run') {
          throw new Error('Diagnostics exist only for a run.');
        }
        return { text: context.diagnosticsText(), fileName: null, capturedAt: downloadCenterIo.now() };
    }
  }

  /**
   * A row's PDF or Word document, rendered by the server; the diagnostics' from the same capture as
   * their Text.
   */
  private async binarySource(
    row: DownloadRow,
    state: DownloadRowState,
    context: DownloadCenterContext,
    cache: Map<string, Promise<SourceText>>,
    paper: BenchmarkPdfPaper,
    format: 'pdf' | 'docx'
  ): Promise<SourceBinary> {
    const service = this.benchmarkService;
    const word = format === 'docx';
    switch (row.kind) {
      case 'pack': {
        const { id } = row.doc!;
        const file = word
          ? service.getReportDocumentDocx(id, state.disclosure, state.naming, paper)
          : service.getReportDocumentPdf(id, state.disclosure, state.naming, paper);
        return { ...await firstValueFrom(file), capturedAt: null };
      }
      case 'runReport': {
        const file = word ? service.getRunReportDocx(row.runId!, paper) : service.getRunReportPdf(row.runId!, paper);
        return { ...await firstValueFrom(file), capturedAt: null };
      }
      case 'toolCallLog': {
        const file = word ? service.getToolCallLogDocx(row.runId!, paper) : service.getToolCallLogPdf(row.runId!, paper);
        return { ...await firstValueFrom(file), capturedAt: null };
      }
      case 'diagnostics': {
        const captured = await this.sourceText(row, state, context, cache);
        const capturedAt = captured.capturedAt ?? downloadCenterIo.now();
        const file = word
          ? service.renderDiagnosticsDocx(row.runId!, captured.text, isoSeconds(capturedAt), paper)
          : service.renderDiagnosticsPdf(row.runId!, captured.text, isoSeconds(capturedAt), paper);
        return { ...await firstValueFrom(file), capturedAt };
      }
    }
  }

  private produceFile(
    row: DownloadRow,
    state: DownloadRowState,
    format: DownloadFormat,
    source: SourceText | SourceBinary,
    context: DownloadCenterContext,
    packagedAt: Date,
    paper: BenchmarkPdfPaper
  ): ProducedFile {
    const internal = row.internalReason !== null
      || (row.kind === 'pack' && state.disclosure === BenchmarkReportDisclosure.Full);
    const bytes = 'bytes' in source ? source.bytes : null;
    let text = 'text' in source ? source.text : '';
    let name: string;
    let mtime: Date;
    let createdAtUtc: string | null;

    if (row.kind === 'pack') {
      const doc = row.doc!;
      name = `${reportDocumentFileStem(doc, row.label)}_${reportDisclosureParam(state.disclosure)}_${reportPeerNamingParam(state.naming)}`
        + `${internal ? '_INTERNAL' : ''}.${format}`;
      if (format === 'html') {
        text = markdownToPrintableHtml(text, doc.title || row.label);
      }
      mtime = utcDate(doc.createdAtUtc) ?? packagedAt;
      createdAtUtc = isoSeconds(mtime);
    } else if (row.kind === 'diagnostics') {
      const run = (context as DownloadCenterRunContext).run;
      name = (format === 'pdf' || format === 'docx') && source.fileName
        ? internalServerName(source.fileName, format)
        : `${safeFileName(run.suiteName)}_${safeFileName(run.modelLabel)}_run${run.id}_diagnostics_INTERNAL.${format}`;
      mtime = source.capturedAt ?? packagedAt;
      createdAtUtc = isoSeconds(mtime);
    } else {
      const fallback = row.kind === 'runReport' ? `benchmark_run${row.runId}_report.md` : `benchmark_run${row.runId}_tool_calls.md`;
      name = internalServerName(source.fileName ?? fallback, format);
      if (format === 'html') {
        text = markdownToPrintableHtml(text, `Benchmark run #${row.runId} report`);
      }
      mtime = runFileTime(row, context, source.fileName) ?? packagedAt;
      createdAtUtc = isoSeconds(mtime);
    }

    const common = {
      name,
      mime: MIME_TYPES[format],
      mtime,
      manifest: {
        name,
        content: bytes ?? text,
        format: FORMAT_LABELS[format],
        pdfPaper: format === 'pdf' ? paper : null,
        wordPaper: format === 'docx' ? paper : null,
        description: row.kind === 'pack' ? audienceLabel(row.doc!.audience) : RUN_FILE_DESCRIPTIONS[row.kind],
        documentId: row.doc?.id ?? null,
        audience: row.doc ? audienceLabel(row.doc.audience) : null,
        disclosure: row.kind === 'pack' ? disclosureLabel(state.disclosure) : null,
        naming: row.kind === 'pack' ? (state.naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized') : null,
        rendererVersion: row.doc?.reportFormatVersion ?? null,
        createdAtUtc,
        writer: row.doc?.writerDisplayName || null,
        internalOnly: internal
      } satisfies ManifestFile
    };
    return bytes ? { ...common, bytes } : { ...common, text };
  }

  /** `<model>_<package>_<yyyyMMdd_HHmmss>.zip`. */
  private zipFileName(context: DownloadCenterContext, packagedAt: Date): string {
    const model = context.kind === 'run'
      ? context.run.modelLabel
      : (this.rows.find(row => row.doc)?.doc?.subjectLabel ?? 'reports');
    return `${safeFileName(model)}_${safeFileName(this.currentPackage.fullName)}_${exportTimestamp(packagedAt)}.zip`;
  }
}

// ---------------------------------------------------------------------------------------------
// Rows
// ---------------------------------------------------------------------------------------------

const RUN_FILE_DESCRIPTIONS: Record<Exclude<DownloadRowKind, 'pack'>, string> = {
  runReport: 'Run report',
  toolCallLog: 'Tool-call log',
  diagnostics: 'Run diagnostics (captured when the download was prepared)'
};

export const ROW_NOTES = {
  toolCallLog: 'Can run to several megabytes; its PDF and Word files can be hundreds of pages.',
  diagnostics: 'Captured when the download is prepared, not stored.'
} as const;

/** The package's formats that the row offers; every format the row offers where none of them is. */
function presetFormats(row: DownloadRow, pkg: 'internal' | 'provider'): DownloadFormat[] {
  const formats = row.formats.filter(format => PRESET_FORMATS[pkg].includes(format));
  return formats.length > 0 ? formats : [...row.formats];
}

function runFileRows(run: DownloadCenterRunInfo): DownloadRow[] {
  const detail = `${run.suiteName} · ${run.modelLabel}`;
  const base = { runId: run.id, doc: null, runChanged: false, allowedDisclosures: [] };
  return [
    {
      ...base, key: `report:${run.id}`, kind: 'runReport', category: 'runReport',
      label: `Run report, run #${run.id}`, detail, note: null,
      formats: ['pdf', 'docx', 'md', 'html'], internalReason: INTERNAL_REASONS.runReport
    },
    {
      ...base, key: `log:${run.id}`, kind: 'toolCallLog', category: 'toolCallLog',
      label: `Tool-call log, run #${run.id}`, detail, note: ROW_NOTES.toolCallLog,
      formats: ['pdf', 'docx', 'md'], internalReason: INTERNAL_REASONS.toolCallLog
    },
    {
      ...base, key: `diag:${run.id}`, kind: 'diagnostics', category: 'diagnostics',
      label: `Run diagnostics, run #${run.id}`, detail, note: ROW_NOTES.diagnostics,
      formats: ['pdf', 'docx', 'txt'], internalReason: INTERNAL_REASONS.diagnostics
    }
  ];
}

function subjectRunReportRow(runId: number, subjectLabel: string): DownloadRow {
  return {
    key: `report:${runId}`,
    kind: 'runReport',
    category: 'runReport',
    label: `Run report, run #${runId}`,
    detail: subjectLabel ? `A subject run of ${subjectLabel}` : 'A subject run',
    note: null,
    runId,
    doc: null,
    runChanged: false,
    allowedDisclosures: [],
    formats: ['pdf', 'docx', 'md', 'html'],
    internalReason: INTERNAL_REASONS.runReport
  };
}

function packRow(doc: BenchmarkReportDocumentListItemDto): DownloadRow {
  const allowed = doc.allowedDisclosures && doc.allowedDisclosures.length > 0
    ? doc.allowedDisclosures
    : doc.audience === BenchmarkReportAudience.InternalBrief ? [BenchmarkReportDisclosure.Full] : [...ALL_DISCLOSURES];
  const shareable = doc.audience !== BenchmarkReportAudience.InternalBrief
    && allowed.some(d => d !== BenchmarkReportDisclosure.Full);
  const created = utcDate(doc.createdAtUtc);
  const parts = [audienceLabel(doc.audience), doc.subjectLabel, doc.writerDisplayName ? `written by ${doc.writerDisplayName}` : '',
    created ? `${isoSeconds(created).slice(0, 16).replace('T', ' ')} UTC` : ''].filter(part => part);
  return {
    key: `doc:${doc.id}`,
    kind: 'pack',
    category: doc.audience === BenchmarkReportAudience.ExecutiveSummary ? 'executiveSummary'
      : doc.audience === BenchmarkReportAudience.TechnicalReport ? 'technicalReport' : 'internalBrief',
    label: doc.title || `${audienceLabel(doc.audience)}: ${doc.subjectLabel}`,
    detail: parts.join(' · '),
    note: null,
    runId: null,
    doc,
    runChanged: !!doc.runChangedSinceGeneration,
    allowedDisclosures: allowed,
    formats: ['pdf', 'docx', 'md', 'html'],
    internalReason: shareable ? null : INTERNAL_REASONS.internalBrief
  };
}

/** Newest first, then by audience, so one pack's documents stay together. */
function sortDocuments(documents: readonly BenchmarkReportDocumentListItemDto[]): BenchmarkReportDocumentListItemDto[] {
  return [...documents].sort((a, b) =>
    (b.createdAtUtc ?? '').localeCompare(a.createdAtUtc ?? '') || a.audience - b.audience || a.id - b.id);
}

// ---------------------------------------------------------------------------------------------
// Names, times and messages
// ---------------------------------------------------------------------------------------------

export function audienceLabel(audience: BenchmarkReportAudience): string {
  switch (audience) {
    case BenchmarkReportAudience.ExecutiveSummary: return 'Executive Summary';
    case BenchmarkReportAudience.TechnicalReport: return 'Report for AI Researchers and Developers';
    case BenchmarkReportAudience.InternalBrief: return 'Internal Improvement Brief';
    default: return 'Report document';
  }
}

/** The audience suffixes a Report for AI Researchers and Developers title can end in, current and earlier. */
const RESEARCHER_REPORT_TITLE_SUFFIX = /\s+[\u2014\u2013-]\s+(?:Report for AI Researchers and Developers|Technical Report)\s*$/;

/**
 * A report document's file-name stem. A Report for AI Researchers and Developers is named by its
 * title without the audience suffix, then `_Researcher_Report`; every other document by its title.
 */
export function reportDocumentFileStem(doc: BenchmarkReportDocumentListItemDto, fallbackTitle: string): string {
  const title = doc.title || fallbackTitle;
  if (doc.audience !== BenchmarkReportAudience.TechnicalReport) {
    return safeFileName(title);
  }
  return `${safeFileName(title.replace(RESEARCHER_REPORT_TITLE_SUFFIX, ''))}_Researcher_Report`;
}

function disclosureLabel(disclosure: BenchmarkReportDisclosure): string {
  switch (disclosure) {
    case BenchmarkReportDisclosure.Summary: return 'Summary';
    case BenchmarkReportDisclosure.Detailed: return 'Detailed';
    default: return 'Full';
  }
}

/**
 * The server's file name with `_INTERNAL` before the extension, unless it already ends so, and the
 * chosen format's extension. Its case is kept; only characters a file system refuses, and path
 * separators, are replaced.
 */
export function internalServerName(serverName: string, format: DownloadFormat): string {
  const clean = serverName.replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_');
  const dot = clean.lastIndexOf('.');
  const base = dot > 0 ? clean.slice(0, dot) : clean;
  return `${base.endsWith('_INTERNAL') ? base : `${base}_INTERNAL`}.${format}`;
}

/** An ISO date from the server, read as UTC when it carries no offset. */
function utcDate(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(value);
  const date = new Date(hasZone ? value : `${value}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

function isoSeconds(date: Date): string {
  return date.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

/**
 * A run file's time: the run's completion, else its start, in run context. In document context the
 * run itself is not fetched, so the start time is read from the report's server file name
 * (`…_yyyyMMdd_HHmmss.md`, or `…_yyyyMMdd_HHmmss_INTERNAL.pdf`, UTC), and null makes the caller use
 * the packaging time.
 */
function runFileTime(row: DownloadRow, context: DownloadCenterContext, serverName: string | null): Date | null {
  if (context.kind === 'run' && context.run.id === row.runId) {
    return utcDate(context.run.completedAtUtc) ?? utcDate(context.run.startedAtUtc);
  }
  const match = /_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})(?:_INTERNAL)?\.[A-Za-z0-9]+$/.exec(serverName ?? '');
  if (!match) {
    return null;
  }
  const [, y, mo, d, h, mi, s] = match.map(Number);
  return new Date(Date.UTC(y, mo - 1, d, h, mi, s));
}

function failureReason(error: unknown, row: DownloadRow): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 404) {
      return row.kind === 'pack' ? 'the document no longer exists' : 'the run no longer exists';
    }
    if (error.status === 0) {
      return 'the server could not be reached';
    }
    const body = typeof error.error === 'string' ? safeJson(error.error) : error.error;
    const message = body && typeof body === 'object' && typeof (body as { error?: unknown }).error === 'string'
      ? (body as { error: string }).error
      : null;
    return message ?? `the server answered ${error.status}`;
  }
  return error instanceof Error && error.message ? error.message : 'it could not be prepared';
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function completionMessage(planned: number, produced: number, failed: number): string {
  if (produced === 0) {
    return planned === 1 ? 'The file could not be prepared; nothing was downloaded.' : 'No file could be prepared; nothing was downloaded.';
  }
  if (planned === 1) {
    return 'Downloaded 1 file.';
  }
  return failed === 0
    ? `Downloaded ${produced} files as one ZIP.`
    : `Downloaded ${produced} of ${planned} files as one ZIP; ${failed} failed.`;
}

/** The paper the Download Center last used, for a PDF opened elsewhere; A4 when none is remembered. */
export function rememberedPdfPaper(): BenchmarkPdfPaper {
  return readStoredSettings()?.paper ?? 'a4';
}

/**
 * The stored settings, or null when absent, unreadable, corrupt or of another version. Version 2
 * reads as version 3 without the formats remembered for the Internal package, so its rows take the
 * Internal preset.
 */
function readStoredSettings(): StoredSettings | null {
  try {
    const raw = localStorage.getItem(DOWNLOAD_CENTER_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as { version?: unknown; package?: unknown; paper?: unknown; packages?: unknown } | null;
    if (!parsed || typeof parsed !== 'object' || (parsed.version !== STORED_SETTINGS_VERSION && parsed.version !== 2)) {
      return null;
    }
    const pkg = DOWNLOAD_PACKAGES.find(p => p.id === parsed.package)?.id;
    const paper = PDF_PAPERS.find(p => p.id === parsed.paper)?.id;
    let packages = parsed.packages && typeof parsed.packages === 'object'
      ? parsed.packages as NonNullable<StoredSettings['packages']>
      : undefined;
    if (parsed.version === 2 && packages?.internal && typeof packages.internal === 'object') {
      const internal: Partial<Record<DownloadRowCategory, StoredChoice>> = {};
      for (const [category, choice] of Object.entries(packages.internal)) {
        if (choice && typeof choice === 'object') {
          const kept: StoredChoice = { ...choice };
          delete kept.formats;
          internal[category as DownloadRowCategory] = kept;
        }
      }
      packages = { ...packages, internal };
    }
    return { version: STORED_SETTINGS_VERSION, package: pkg, paper, packages };
  } catch {
    return null;
  }
}
