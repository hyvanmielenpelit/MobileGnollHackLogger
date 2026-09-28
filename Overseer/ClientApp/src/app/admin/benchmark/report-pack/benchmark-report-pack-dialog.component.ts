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
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import type { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDisclosure,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackPricingBasis,
  BenchmarkReportPackRequest,
  BenchmarkReportPeerNaming,
  BenchmarkReportValidationNote,
  SameProviderWarningDto
} from '../../../services/admin-benchmark.service';
import { AdminService, SystemAiConfigDto } from '../../../services/admin.service';
import { ModelPickerComponent, ModelPickerKey, ModelPickerOption, toModelPickerOptions } from '../../../shared/model-picker/model-picker.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { TableState, exactFilter } from '../../../shared/data-table/table-state';
import { SortHeaderComponent } from '../../../shared/data-table/sort-header.component';
import { TablePagerComponent } from '../../../shared/data-table/table-pager.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import { BenchmarkDownloadCenterComponent } from '../download-center/benchmark-download-center.component';
import { markdownToSafeHtmlFragment } from '../download-center/printable-html';
import type {
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis
} from '../model-comparison/model-comparison.models';

/** What the Report Pack dialog is opened on: the comparison request, its entries and its suite. */
export interface ReportPackContext {
  readonly runIds: readonly number[];
  readonly groupIds: readonly number[];
  readonly pricingBasis: BenchmarkModelComparisonPricingBasis;
  /** Every entry of the comparison; the non-Excluded ones are the possible subjects. */
  readonly entries: readonly BenchmarkModelComparisonEntryDto[];
  readonly suiteId: number | null;
  readonly suiteName: string | null;
}

/** One document the New report pack form offers. */
export interface ReportPackAudienceOption {
  readonly audience: BenchmarkReportAudience;
  readonly label: string;
  readonly description: string;
  readonly checkedByDefault: boolean;
}

/** A subject the form offers: a non-Excluded entry of the comparison. */
export interface ReportPackSubjectOption {
  readonly key: string;
  readonly label: string;
}

export type ReportPackPreviewAxis = 'disclosure' | 'naming';

export const REPORT_PACK_AUDIENCES: readonly ReportPackAudienceOption[] = [
  {
    audience: BenchmarkReportAudience.ExecutiveSummary,
    label: 'Executive Summary',
    description: 'For a non-specialist at the model’s provider, or a manager: plain language, short.',
    checkedByDefault: true
  },
  {
    audience: BenchmarkReportAudience.TechnicalReport,
    label: 'Technical Report',
    description: 'For AI researchers and model developers: figures against the peers, strengths, weaknesses and recommendations.',
    checkedByDefault: true
  },
  {
    audience: BenchmarkReportAudience.InternalBrief,
    label: 'Internal Improvement Brief',
    description: 'For the Overseer team: what to improve in the chat, the benchmark and the model. Internal only, at Full disclosure.',
    checkedByDefault: false
  }
];

export const DISCLOSURE_ORDER: readonly BenchmarkReportDisclosure[] = [
  BenchmarkReportDisclosure.Summary,
  BenchmarkReportDisclosure.Detailed,
  BenchmarkReportDisclosure.Full
];

export const NAMING_ORDER: readonly BenchmarkReportPeerNaming[] = [
  BenchmarkReportPeerNaming.Anonymized,
  BenchmarkReportPeerNaming.Named
];

/** The wait after the last change of subject, documents or writer before the estimate is requested. */
export const REPORT_PACK_PREVIEW_DEBOUNCE_MS = 400;

/** The interval between two progress requests while a job runs. */
export const REPORT_PACK_POLL_MS = 2000;

/** The longest wait between progress requests after repeated failures. */
export const REPORT_PACK_POLL_MAX_BACKOFF_MS = 30000;

/** The per-viewer memory of the last writer used. */
export const REPORT_PACK_STORAGE_KEY = 'overseer.benchmark.reportPack';

/** The job statuses after which nothing changes. */
const FINISHED_JOB_STATUSES = new Set(['Completed', 'CompletedWithErrors', 'Canceled', 'Failed']);

/**
 * The Markdown-to-HTML step of the preview. A holder, so a spec can observe each conversion; it is the
 * Download Center's converter, so what is previewed is what an HTML download contains.
 */
export const reportPackPreviewIo = {
  toHtml: (markdown: string): string => markdownToSafeHtmlFragment(markdown)
};

export function audienceLabel(audience: BenchmarkReportAudience): string {
  return REPORT_PACK_AUDIENCES.find(option => option.audience === audience)?.label ?? `Document ${audience}`;
}

export function disclosureLabel(disclosure: BenchmarkReportDisclosure): string {
  switch (disclosure) {
    case BenchmarkReportDisclosure.Detailed: return 'Detailed';
    case BenchmarkReportDisclosure.Full: return 'Full';
    default: return 'Summary';
  }
}

export function disclosureDescription(disclosure: BenchmarkReportDisclosure): string {
  switch (disclosure) {
    case BenchmarkReportDisclosure.Detailed:
      return 'Detailed: verbatim question text and answer excerpts for the questions the notes discuss; no rubrics or grader evidence.';
    case BenchmarkReportDisclosure.Full:
      return 'Full: everything, rubrics and grader evidence included. Internal only.';
    default:
      return 'Summary: questions described by topic; no question text, rubric, answer or grader evidence.';
  }
}

export function namingLabel(naming: BenchmarkReportPeerNaming): string {
  return naming === BenchmarkReportPeerNaming.Named ? 'Named' : 'Anonymized';
}

/** An entry's option text, with the axes a Degraded entry is degraded on. */
export function subjectOptionLabel(entry: BenchmarkModelComparisonEntryDto): string {
  if (entry.state !== 'Degraded' && !entry.speedDegraded && !entry.costDegraded) {
    return entry.label;
  }
  const axes = [entry.speedDegraded ? 'speed' : null, entry.costDegraded ? 'cost' : null]
    .filter((axis): axis is string => axis !== null);
  return axes.length > 0 ? `${entry.label} (${axes.join(' and ')} degraded)` : `${entry.label} (degraded)`;
}

/** `CompletedWithWarnings` → `Completed with warnings`. */
export function statusLabel(status: string | null | undefined): string {
  if (!status) {
    return '';
  }
  const words = status.replace(/([a-z])([A-Z])/g, '$1 $2').split(' ');
  return words.map((word, i) => (i === 0 ? word : word.toLowerCase())).join(' ');
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

/** `42 s`, `3 min 05 s`, `1 h 02 min`. */
export function formatElapsed(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  if (seconds < 60) {
    return `${seconds} s`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min ${String(seconds % 60).padStart(2, '0')} s`;
  }
  return `${Math.floor(minutes / 60)} h ${String(minutes % 60).padStart(2, '0')} min`;
}

function isJobDto(body: unknown): body is BenchmarkReportPackJobDto {
  return !!body && typeof body === 'object'
    && typeof (body as BenchmarkReportPackJobDto).id === 'string'
    && Array.isArray((body as BenchmarkReportPackJobDto).documents);
}

function isSameProviderWarning(body: unknown): body is SameProviderWarningDto {
  return !!body && typeof body === 'object' && typeof (body as SameProviderWarningDto).message === 'string';
}

/** The `{ error }` or plain-string message of a refused request, or null. */
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

function readStoredWriterId(): number | null {
  try {
    const raw = localStorage.getItem(REPORT_PACK_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as { writerConfigId?: unknown };
    return typeof parsed?.writerConfigId === 'number' ? parsed.writerConfigId : null;
  } catch {
    return null;
  }
}

function writeStoredWriterId(writerConfigId: number): void {
  try {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ writerConfigId }));
  } catch {
    // Storage unavailable: the writer is simply not remembered.
  }
}

/**
 * The Report Pack dialog, full-screen over the Model Comparison wizard: the left column starts a
 * report pack for one entry of the comparison, and follows its job; the right column lists the stored
 * documents of the comparison's suite, with preview, download (through the Download Center) and delete.
 *
 * `open(context)` shows it; `closed` fires however it closes. The dialog is a DOM descendant of the
 * wizard's `<dialog>`, so it stops its own close and cancel events, and those of the dialogs nested in
 * it, from reaching the wizard.
 */
@Component({
  selector: 'app-benchmark-report-pack-dialog',
  standalone: true,
  imports: [
    RunReportFrameComponent, BenchmarkDownloadCenterComponent, ModelPickerComponent, InfoTipComponent,
    SortHeaderComponent, TablePagerComponent
  ],
  templateUrl: './benchmark-report-pack-dialog.component.html',
  styleUrls: ['./benchmark-report-pack-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkReportPackDialogComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly adminService = inject(AdminService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('reportPackDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('reportPackHeading') heading?: ElementRef<HTMLElement>;
  @ViewChild('previewDialog') previewDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('deleteDialog') deleteDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('downloadCenter') downloadCenter?: BenchmarkDownloadCenterComponent;

  /** Emitted when the dialog closes, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();

  /** The writer tip's *How the graders work* link: the host opens the guide at *Choosing grader models*. */
  @Output() readonly graderGuideRequested = new EventEmitter<void>();

  readonly audiences = REPORT_PACK_AUDIENCES;
  readonly audienceLabel = audienceLabel;
  readonly statusLabel = statusLabel;
  readonly formatCostUsd = formatCostUsd;
  readonly formatUtc = formatUtc;
  readonly disclosureLabel = disclosureLabel;
  readonly namingLabel = namingLabel;
  readonly writerEmptyHint =
    'No system AI configs with the Benchmark role are enabled. Enable the Benchmark role in System Configs.';

  context: ReportPackContext | null = null;

  // --- New report pack ---
  subjects: ReportPackSubjectOption[] = [];
  subjectKey: string | null = null;
  private readonly checkedAudiences = new Set<BenchmarkReportAudience>();

  writers: SystemAiConfigDto[] = [];
  writersLoading = false;
  writersError: string | null = null;
  writerId: number | null = null;
  private writerOptionsSource: SystemAiConfigDto[] | null = null;
  private writerOptionsCache: ModelPickerOption<SystemAiConfigDto>[] = [];

  preview: BenchmarkReportPackPreviewDto | null = null;
  /** A change is waiting out the debounce, or its estimate is in flight. */
  previewPending = false;
  previewError: string | null = null;
  /** The same-provider warning a 409 carried, shown until the subject or writer changes. */
  private serverWarning: string | null = null;
  acknowledgeSameProvider = false;

  starting = false;
  startError: string | null = null;

  /** The job this dialog follows, and its last known state. */
  activeJobId: string | null = null;
  job: BenchmarkReportPackJobDto | null = null;
  /** When `job` was last read, for the elapsed time of a running job. */
  jobReadAt = 0;
  pollError: string | null = null;
  canceling = false;

  // --- Documents ---
  documents: BenchmarkReportDocumentListItemDto[] = [];
  documentsLoading = false;
  documentsError: string | null = null;
  libraryStatus = '';
  readonly selectedIds = new Set<number>();
  readonly documentTable = new TableState<BenchmarkReportDocumentListItemDto>('createdAtUtc', 'desc').registerAccessors(
    {
      createdAtUtc: d => d.createdAtUtc,
      subjectLabel: d => d.subjectLabel,
      audience: d => audienceLabel(d.audience),
      writerDisplayName: d => d.writerDisplayName,
      status: d => statusLabel(d.status),
      costUsd: d => d.costUsd ?? null
    },
    { selected: exactFilter(d => (this.selectedIds.has(d.id) ? 'yes' : 'no')) }
  );

  // --- Preview ---
  previewDoc: BenchmarkReportDocumentListItemDto | null = null;
  previewDisclosure: BenchmarkReportDisclosure = BenchmarkReportDisclosure.Summary;
  previewNaming: BenchmarkReportPeerNaming = BenchmarkReportPeerNaming.Anonymized;
  previewHtml: SafeHtml | null = null;
  previewRendering = false;
  previewRenderError: string | null = null;
  previewNotes: BenchmarkReportValidationNote[] | null = null;

  // --- Delete ---
  deleteTarget: BenchmarkReportDocumentListItemDto | null = null;
  deleting = false;
  deleteError: string | null = null;

  /** Where focus returns when a nested dialog closes. */
  private returnFocusId: string | null = null;

  /** Bumped on every open and close, so a response for an earlier opening never lands. */
  private generation = 0;
  private previewTimer: ReturnType<typeof setTimeout> | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollFailures = 0;
  /** The job whose finish already refreshed the documents list. */
  private refreshedForJobId: string | null = null;
  private renderGeneration = 0;
  private readonly subscriptions: Record<string, Subscription | undefined> = {};

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.generation++;
    this.stopTimers();
  }

  // -------------------------------------------------------------------------------------------
  // Opening and closing
  // -------------------------------------------------------------------------------------------

  /** Shows the dialog for a comparison: its first non-Excluded entry as the subject, Executive and Technical checked. */
  open(context: ReportPackContext): void {
    this.generation++;
    this.stopTimers();
    this.context = context;
    this.subjects = context.entries
      .filter(entry => !entry.excluded && entry.state !== 'Excluded')
      .map(entry => ({ key: entry.key, label: subjectOptionLabel(entry) }));
    this.subjectKey = this.subjects[0]?.key ?? null;
    this.checkedAudiences.clear();
    for (const option of REPORT_PACK_AUDIENCES) {
      if (option.checkedByDefault) {
        this.checkedAudiences.add(option.audience);
      }
    }
    this.writerId = null;
    this.preview = null;
    this.previewPending = false;
    this.previewError = null;
    this.serverWarning = null;
    this.acknowledgeSameProvider = false;
    this.starting = false;
    this.startError = null;
    this.activeJobId = null;
    this.job = null;
    this.pollError = null;
    this.pollFailures = 0;
    this.canceling = false;
    this.refreshedForJobId = null;
    this.documents = [];
    this.selectedIds.clear();
    this.documentTable.clearFilters();
    this.documentTable.page = 1;
    this.libraryStatus = '';

    const generation = this.generation;
    this.loadWriters(generation);
    this.loadDocuments();
    this.loadActiveJob(generation);

    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.heading?.nativeElement.focus();
    this.cdr.markForCheck();
  }

  close(): void {
    this.dialog?.nativeElement?.close();
  }

  /** The dialog's close and cancel events; neither may reach the wizard's dialog around it. */
  onDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.dialog?.nativeElement?.open) {
      return;
    }
    this.generation++;
    this.stopTimers();
    this.cdr.markForCheck();
    this.closed.emit();
  }

  /** A nested dialog's cancel event, stopped so it reaches neither this dialog nor the wizard. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  get subtitle(): string {
    const suite = this.context?.suiteName || 'Suite not set';
    const count = this.subjects.length;
    return `${suite} · ${count} possible ${count === 1 ? 'subject' : 'subjects'}`;
  }

  requestGraderGuide(): void {
    this.graderGuideRequested.emit();
  }

  // -------------------------------------------------------------------------------------------
  // The form: subject, documents, writer
  // -------------------------------------------------------------------------------------------

  selectSubject(key: string): void {
    if (key === this.subjectKey) {
      return;
    }
    this.subjectKey = key;
    this.acknowledgeSameProvider = false;
    this.serverWarning = null;
    this.startError = null;
    this.schedulePreview();
  }

  onSubjectChange(event: Event): void {
    this.selectSubject((event.target as HTMLSelectElement).value);
  }

  isAudienceChecked(audience: BenchmarkReportAudience): boolean {
    return this.checkedAudiences.has(audience);
  }

  setAudience(audience: BenchmarkReportAudience, checked: boolean): void {
    if (checked) {
      this.checkedAudiences.add(audience);
    } else {
      this.checkedAudiences.delete(audience);
    }
    this.startError = null;
    this.schedulePreview();
  }

  onAudienceChange(audience: BenchmarkReportAudience, event: Event): void {
    this.setAudience(audience, (event.target as HTMLInputElement).checked);
  }

  /** The checked documents, in the fixed audience order. */
  get selectedAudiences(): BenchmarkReportAudience[] {
    return REPORT_PACK_AUDIENCES.map(option => option.audience).filter(audience => this.checkedAudiences.has(audience));
  }

  get writerOptions(): ModelPickerOption<SystemAiConfigDto>[] {
    if (this.writerOptionsSource !== this.writers) {
      this.writerOptionsSource = this.writers;
      this.writerOptionsCache = toModelPickerOptions(this.writers);
    }
    return this.writerOptionsCache;
  }

  selectWriter(key: ModelPickerKey | null): void {
    const id = typeof key === 'number' ? key : null;
    if (id === this.writerId) {
      return;
    }
    this.writerId = id;
    this.acknowledgeSameProvider = false;
    this.serverWarning = null;
    this.startError = null;
    this.schedulePreview();
  }

  get selectedWriter(): SystemAiConfigDto | null {
    return this.writers.find(writer => writer.id === this.writerId) ?? null;
  }

  /** The preview's same-provider warning, or the one a 409 carried. */
  get sameProviderWarning(): string | null {
    return this.preview?.sameProviderWarning ?? this.serverWarning;
  }

  setAcknowledgeSameProvider(checked: boolean): void {
    this.acknowledgeSameProvider = checked;
    this.startError = null;
    this.cdr.markForCheck();
  }

  onAcknowledgeChange(event: Event): void {
    this.setAcknowledgeSameProvider((event.target as HTMLInputElement).checked);
  }

  /** The request body, or null while the form is incomplete. */
  buildRequest(): BenchmarkReportPackRequest | null {
    const context = this.context;
    if (!context || this.subjectKey === null || this.writerId === null) {
      return null;
    }
    return {
      runIds: [...context.runIds],
      groupIds: [...context.groupIds],
      pricingBasis: context.pricingBasis === 'AsRun'
        ? BenchmarkReportPackPricingBasis.AsRun
        : BenchmarkReportPackPricingBasis.Current,
      subjectKey: this.subjectKey,
      audiences: this.selectedAudiences,
      writerModelConfigurationId: this.writerId,
      acknowledgeSameProvider: this.acknowledgeSameProvider
    };
  }

  // -------------------------------------------------------------------------------------------
  // The estimate
  // -------------------------------------------------------------------------------------------

  /** Requests a new estimate after the debounce; any estimate in flight is dropped. */
  schedulePreview(): void {
    this.clearPreviewTimer();
    this.subscriptions['preview']?.unsubscribe();
    this.preview = null;
    this.previewError = null;
    const ready = this.subjectKey !== null && this.writerId !== null && this.selectedAudiences.length > 0;
    this.previewPending = ready;
    if (ready) {
      this.previewTimer = setTimeout(() => {
        this.previewTimer = null;
        this.requestPreview();
      }, REPORT_PACK_PREVIEW_DEBOUNCE_MS);
    }
    this.cdr.markForCheck();
  }

  retryPreview(): void {
    this.schedulePreview();
  }

  private requestPreview(): void {
    const request = this.buildRequest();
    if (!request || request.audiences.length === 0) {
      this.previewPending = false;
      this.cdr.markForCheck();
      return;
    }
    const generation = this.generation;
    this.subscriptions['preview'] = this.benchmarkService.previewReportPack(request).subscribe({
      next: preview => {
        if (generation !== this.generation) {
          return;
        }
        this.preview = preview;
        this.previewPending = false;
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.previewPending = false;
        this.previewError = serverMessage(error) ?? 'The estimate could not be computed.';
        this.cdr.markForCheck();
      }
    });
  }

  /** One line per checked document: its estimated tokens and cost. */
  get estimateRows(): { label: string; tokens: string; cost: string }[] {
    return (this.preview?.estimates ?? []).map(estimate => ({
      label: audienceLabel(estimate.audience),
      tokens: `${estimate.estimatedInputTokens.toLocaleString('en-US')} in · ${estimate.estimatedOutputTokens.toLocaleString('en-US')} out`,
      cost: formatCostUsd(estimate.estimatedCostUsd)
    }));
  }

  get estimateTotal(): string {
    return formatCostUsd(this.preview?.estimatedTotalCostUsd ?? null);
  }

  // -------------------------------------------------------------------------------------------
  // Generate
  // -------------------------------------------------------------------------------------------

  /** Why Generate is unavailable, or null when it can start. */
  get generateBlockedReason(): string | null {
    if (this.jobInProgress) {
      return 'A report pack is being written. Wait for it to finish, or cancel it.';
    }
    if (this.subjectKey === null) {
      return 'Every entry of this comparison is Excluded, so none can be the subject.';
    }
    if (this.selectedAudiences.length === 0) {
      return 'Choose at least one document.';
    }
    if (this.writerId === null) {
      return 'Choose a report writer.';
    }
    if (this.previewPending) {
      return 'Checking the writer and estimating the cost…';
    }
    if (this.previewError) {
      return 'The estimate could not be computed. Retry it before generating.';
    }
    if (this.preview?.refusal) {
      return 'This writer is refused for this subject. Choose another writer.';
    }
    if (this.sameProviderWarning && !this.acknowledgeSameProvider) {
      return 'Acknowledge the same-provider warning first.';
    }
    return null;
  }

  generate(): void {
    const request = this.buildRequest();
    if (!request || this.generateBlockedReason !== null) {
      return;
    }
    this.starting = true;
    this.startError = null;
    this.pollError = null;
    writeStoredWriterId(request.writerModelConfigurationId);
    const generation = this.generation;
    this.subscriptions['start'] = this.benchmarkService.startReportPack(request).subscribe({
      next: response => {
        if (generation !== this.generation) {
          return;
        }
        this.starting = false;
        this.activeJobId = response.jobId;
        this.job = null;
        this.refreshedForJobId = null;
        this.pollFailures = 0;
        this.pollJob();
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.starting = false;
        this.startError = this.startErrorMessage(error);
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  private startErrorMessage(error: HttpErrorResponse): string {
    const body = error.error;
    if (error.status === 409 && isJobDto(body)) {
      this.adoptJob(body);
      return `Another report pack is being written, for ${body.subjectLabel}, started ${formatUtc(body.startedAtUtc)}. `
        + 'One job runs at a time; its progress is shown below.';
    }
    if (error.status === 409 && isSameProviderWarning(body)) {
      this.serverWarning = body.message;
      this.acknowledgeSameProvider = false;
      return body.message;
    }
    if (error.status === 429) {
      return serverMessage(error) ?? 'The benchmark spend cap does not allow this report pack now.';
    }
    return serverMessage(error) ?? 'The report pack could not be started.';
  }

  // -------------------------------------------------------------------------------------------
  // The job
  // -------------------------------------------------------------------------------------------

  /** Starting, or following a job that has not finished. */
  get jobInProgress(): boolean {
    if (this.starting) {
      return true;
    }
    if (this.activeJobId === null) {
      return false;
    }
    return this.job === null || !FINISHED_JOB_STATUSES.has(this.job.status);
  }

  get jobDocuments(): BenchmarkReportPackDocumentProgressDto[] {
    return this.job?.documents ?? [];
  }

  get jobElapsed(): string {
    const job = this.job;
    if (!job) {
      return '';
    }
    const start = Date.parse(job.startedAtUtc);
    const end = job.completedAtUtc ? Date.parse(job.completedAtUtc) : this.jobReadAt;
    return Number.isNaN(start) || Number.isNaN(end) ? '' : formatElapsed(end - start);
  }

  /** The live line: announced as the job advances. */
  get jobStatusLine(): string {
    const job = this.job;
    if (!job) {
      return this.activeJobId ? 'Starting the report pack…' : '';
    }
    const total = job.documents.length;
    const finished = job.documents.filter(d => FINISHED_JOB_STATUSES.has(d.status)
      || d.status === 'CompletedWithWarnings').length;
    if (job.status === 'Running') {
      return `Writing ${total} ${total === 1 ? 'document' : 'documents'} for ${job.subjectLabel}: ${finished} of ${total} finished.`;
    }
    return `Report pack for ${job.subjectLabel}: ${statusLabel(job.status)}.`;
  }

  cancelJob(): void {
    const id = this.activeJobId;
    if (!id || !this.jobInProgress || this.canceling) {
      return;
    }
    this.canceling = true;
    const generation = this.generation;
    this.subscriptions['cancel'] = this.benchmarkService.cancelReportPackJob(id).subscribe({
      next: () => {
        if (generation !== this.generation) {
          return;
        }
        this.canceling = false;
        this.pollJob();
        this.cdr.markForCheck();
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.generation) {
          return;
        }
        this.canceling = false;
        this.pollError = serverMessage(error) ?? 'The job could not be canceled.';
        this.cdr.markForCheck();
      }
    });
    this.cdr.markForCheck();
  }

  private loadActiveJob(generation: number): void {
    this.subscriptions['active'] = this.benchmarkService.getActiveReportPackJob().subscribe({
      next: job => {
        if (generation !== this.generation || !job || this.activeJobId !== null) {
          return;
        }
        this.adoptJob(job);
        this.cdr.markForCheck();
      },
      error: () => {
        // No running job can be shown; starting one reports the conflict if there is one.
      }
    });
  }

  private adoptJob(job: BenchmarkReportPackJobDto): void {
    this.activeJobId = job.id;
    this.refreshedForJobId = null;
    this.pollFailures = 0;
    this.applyJob(job);
  }

  private applyJob(job: BenchmarkReportPackJobDto): void {
    this.job = job;
    this.jobReadAt = Date.now();
    this.pollError = null;
    if (FINISHED_JOB_STATUSES.has(job.status)) {
      this.clearPollTimer();
      if (this.refreshedForJobId !== job.id) {
        this.refreshedForJobId = job.id;
        this.loadDocuments();
      }
    } else {
      this.schedulePoll(REPORT_PACK_POLL_MS);
    }
  }

  private schedulePoll(delay: number): void {
    this.clearPollTimer();
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      this.pollJob();
    }, delay);
  }

  /** One progress request; the next is scheduled only when it answers, so requests never stack. */
  private pollJob(): void {
    const id = this.activeJobId;
    if (!id) {
      return;
    }
    this.clearPollTimer();
    this.subscriptions['poll']?.unsubscribe();
    const generation = this.generation;
    this.subscriptions['poll'] = this.benchmarkService.getReportPackJob(id).subscribe({
      next: job => {
        if (generation !== this.generation) {
          return;
        }
        this.pollFailures = 0;
        this.applyJob(job);
        this.cdr.markForCheck();
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }
        this.pollFailures++;
        this.pollError = 'The job’s progress could not be read. Retrying.';
        this.schedulePoll(Math.min(REPORT_PACK_POLL_MS * 2 ** this.pollFailures, REPORT_PACK_POLL_MAX_BACKOFF_MS));
        this.cdr.markForCheck();
      }
    });
  }

  // -------------------------------------------------------------------------------------------
  // Writers
  // -------------------------------------------------------------------------------------------

  private loadWriters(generation: number): void {
    this.writersLoading = true;
    this.writersError = null;
    this.subscriptions['writers'] = this.adminService.getSystemConfigs().subscribe({
      next: configs => {
        if (generation !== this.generation) {
          return;
        }
        this.writersLoading = false;
        this.writers = (configs ?? []).filter(c => (c.modelRole & 4) === 4 && c.hasApiKey && c.isEnabled);
        const remembered = readStoredWriterId();
        if (this.writerId === null && remembered !== null && this.writers.some(w => w.id === remembered)) {
          this.selectWriter(remembered);
        }
        this.cdr.markForCheck();
      },
      error: () => {
        if (generation !== this.generation) {
          return;
        }
        this.writersLoading = false;
        this.writersError = 'The report writers could not be loaded.';
        this.cdr.markForCheck();
      }
    });
  }

  // -------------------------------------------------------------------------------------------
  // Documents
  // -------------------------------------------------------------------------------------------

  loadDocuments(): void {
    const generation = this.generation;
    this.documentsLoading = true;
    this.documentsError = null;
    this.subscriptions['documents']?.unsubscribe();
    this.subscriptions['documents'] = this.benchmarkService
      .listReportDocuments({ suiteId: this.context?.suiteId ?? null })
      .subscribe({
        next: documents => {
          if (generation !== this.generation) {
            return;
          }
          this.documentsLoading = false;
          this.documents = documents ?? [];
          const present = new Set(this.documents.map(d => d.id));
          for (const id of [...this.selectedIds]) {
            if (!present.has(id)) {
              this.selectedIds.delete(id);
            }
          }
          this.cdr.markForCheck();
        },
        error: (error: HttpErrorResponse) => {
          if (generation !== this.generation) {
            return;
          }
          this.documentsLoading = false;
          this.documentsError = serverMessage(error) ?? 'The report documents could not be loaded.';
          this.cdr.markForCheck();
        }
      });
  }

  get documentView(): BenchmarkReportDocumentListItemDto[] {
    return this.documentTable.view(this.documents);
  }

  onTableChanged(): void {
    this.cdr.markForCheck();
  }

  documentName(doc: BenchmarkReportDocumentListItemDto): string {
    return `${doc.title}, ${formatUtc(doc.createdAtUtc)}`;
  }

  isSelected(doc: BenchmarkReportDocumentListItemDto): boolean {
    return this.selectedIds.has(doc.id);
  }

  toggleSelected(doc: BenchmarkReportDocumentListItemDto, checked: boolean): void {
    if (checked) {
      this.selectedIds.add(doc.id);
    } else {
      this.selectedIds.delete(doc.id);
    }
    this.cdr.markForCheck();
  }

  onSelectChange(doc: BenchmarkReportDocumentListItemDto, event: Event): void {
    this.toggleSelected(doc, (event.target as HTMLInputElement).checked);
  }

  clearSelection(): void {
    this.selectedIds.clear();
    if (this.showSelectedOnly) {
      this.documentTable.setFilter('selected', '');
    }
    this.cdr.markForCheck();
  }

  /** Selected documents the current page does not show. */
  get offPageSelectedCount(): number {
    const onPage = new Set(this.documentView.map(d => d.id));
    return [...this.selectedIds].filter(id => !onPage.has(id)).length;
  }

  get showSelectedOnly(): boolean {
    return this.documentTable.filters['selected'] === 'yes';
  }

  toggleShowSelectedOnly(): void {
    this.documentTable.setFilter('selected', this.showSelectedOnly ? '' : 'yes');
    this.cdr.markForCheck();
  }

  /** The selected ids in the list's order. */
  get selectedDocumentIds(): number[] {
    return this.documents.filter(d => this.selectedIds.has(d.id)).map(d => d.id);
  }

  // -------------------------------------------------------------------------------------------
  // Download
  // -------------------------------------------------------------------------------------------

  downloadDocument(doc: BenchmarkReportDocumentListItemDto): void {
    this.openDownloadCenter([doc.id], `rp-doc-${doc.id}-download`);
  }

  downloadSelected(): void {
    const ids = this.selectedDocumentIds;
    if (ids.length === 0) {
      return;
    }
    this.openDownloadCenter(ids, 'rp-download-selected');
  }

  private openDownloadCenter(documentIds: number[], returnFocusId: string): void {
    this.returnFocusId = returnFocusId;
    this.downloadCenter?.open({ kind: 'documents', documentIds });
  }

  onDownloadCenterClosed(): void {
    this.restoreFocus();
  }

  // -------------------------------------------------------------------------------------------
  // Preview
  // -------------------------------------------------------------------------------------------

  /** The disclosures this document renders at, in the fixed order. */
  get previewDisclosures(): BenchmarkReportDisclosure[] {
    const allowed = this.previewDoc?.allowedDisclosures ?? [];
    return DISCLOSURE_ORDER.filter(d => allowed.includes(d));
  }

  readonly previewNamings = NAMING_ORDER;

  get previewDisclosureDescription(): string {
    return disclosureDescription(this.previewDisclosure);
  }

  openPreview(doc: BenchmarkReportDocumentListItemDto): void {
    this.previewDoc = doc;
    this.returnFocusId = `rp-doc-${doc.id}-preview`;
    const disclosures = this.previewDisclosures;
    this.previewDisclosure = disclosures[0] ?? BenchmarkReportDisclosure.Summary;
    this.previewNaming = BenchmarkReportPeerNaming.Anonymized;
    this.previewHtml = null;
    this.previewNotes = null;
    this.previewRenderError = null;
    this.loadPreviewNotes(doc.id);
    this.renderPreview();
    const dialog = this.previewDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cdr.markForCheck();
  }

  setPreviewDisclosure(disclosure: BenchmarkReportDisclosure): void {
    if (disclosure === this.previewDisclosure || !this.previewDisclosures.includes(disclosure)) {
      return;
    }
    this.previewDisclosure = disclosure;
    this.renderPreview();
  }

  setPreviewNaming(naming: BenchmarkReportPeerNaming): void {
    if (naming === this.previewNaming) {
      return;
    }
    this.previewNaming = naming;
    this.renderPreview();
  }

  /** Arrow keys, Home and End in either segmented row; focus follows selection. */
  onSegmentKeydown(event: KeyboardEvent, axis: ReportPackPreviewAxis, index: number): void {
    const count = axis === 'disclosure' ? this.previewDisclosures.length : this.previewNamings.length;
    const targets: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: count - 1 };
    const requested = targets[event.key];
    if (requested === undefined || count === 0) {
      return;
    }
    event.preventDefault();
    const next = (requested + count) % count;
    if (axis === 'disclosure') {
      const disclosure = this.previewDisclosures[next];
      this.setPreviewDisclosure(disclosure);
      this.cdr.detectChanges();
      document.getElementById(`rp-preview-disclosure-${disclosure}`)?.focus();
    } else {
      const naming = this.previewNamings[next];
      this.setPreviewNaming(naming);
      this.cdr.detectChanges();
      document.getElementById(`rp-preview-naming-${naming}`)?.focus();
    }
  }

  closePreview(): void {
    this.previewDialog?.nativeElement?.close();
  }

  onPreviewDialogEvent(event: Event): void {
    event.stopPropagation();
    if (event.type !== 'close' || this.previewDialog?.nativeElement?.open) {
      return;
    }
    this.renderGeneration++;
    this.subscriptions['render']?.unsubscribe();
    this.subscriptions['notes']?.unsubscribe();
    this.previewDoc = null;
    this.previewHtml = null;
    this.previewRendering = false;
    this.cdr.markForCheck();
    this.restoreFocus();
  }

  private renderPreview(): void {
    const doc = this.previewDoc;
    if (!doc) {
      return;
    }
    const render = ++this.renderGeneration;
    this.previewRendering = true;
    this.previewRenderError = null;
    this.subscriptions['render']?.unsubscribe();
    this.subscriptions['render'] = this.benchmarkService
      .renderReportDocument(doc.id, this.previewDisclosure, this.previewNaming)
      .subscribe({
        next: markdown => {
          if (render !== this.renderGeneration) {
            return;
          }
          this.previewRendering = false;
          // Already sanitized by the Download Center's private DOMPurify; Angular's own sanitizer
          // would alter it further, and the preview would no longer match the downloaded file.
          this.previewHtml = this.sanitizer.bypassSecurityTrustHtml(reportPackPreviewIo.toHtml(markdown ?? ''));
          this.cdr.markForCheck();
        },
        error: (error: HttpErrorResponse) => {
          if (render !== this.renderGeneration) {
            return;
          }
          this.previewRendering = false;
          this.previewHtml = null;
          this.previewRenderError = serverMessage(error) ?? 'The document could not be rendered.';
          this.cdr.markForCheck();
        }
      });
    this.cdr.markForCheck();
  }

  private loadPreviewNotes(id: number): void {
    this.subscriptions['notes']?.unsubscribe();
    this.subscriptions['notes'] = this.benchmarkService.getReportDocument(id).subscribe({
      next: detail => {
        if (this.previewDoc?.id !== id) {
          return;
        }
        this.previewNotes = detail.validationNotes ?? [];
        this.cdr.markForCheck();
      },
      error: () => {
        // The notes are supplementary; the rendered document is still shown.
      }
    });
  }

  // -------------------------------------------------------------------------------------------
  // Delete
  // -------------------------------------------------------------------------------------------

  requestDelete(doc: BenchmarkReportDocumentListItemDto): void {
    this.deleteTarget = doc;
    this.deleteError = null;
    this.deleting = false;
    this.returnFocusId = `rp-doc-${doc.id}-delete`;
    const dialog = this.deleteDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cdr.markForCheck();
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
    this.subscriptions['delete'] = this.benchmarkService.deleteReportDocument(doc.id).subscribe({
      next: () => {
        if (generation !== this.generation) {
          return;
        }
        this.deleting = false;
        this.documents = this.documents.filter(d => d.id !== doc.id);
        this.selectedIds.delete(doc.id);
        this.libraryStatus = `Deleted ${this.documentName(doc)}.`;
        this.returnFocusId = 'rp-documents-heading';
        this.deleteDialog?.nativeElement?.close();
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
  // Housekeeping
  // -------------------------------------------------------------------------------------------

  private restoreFocus(): void {
    const id = this.returnFocusId;
    this.returnFocusId = null;
    if (id) {
      document.getElementById(id)?.focus();
    }
  }

  private clearPreviewTimer(): void {
    if (this.previewTimer !== null) {
      clearTimeout(this.previewTimer);
      this.previewTimer = null;
    }
  }

  private clearPollTimer(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private stopTimers(): void {
    this.clearPreviewTimer();
    this.clearPollTimer();
    for (const key of Object.keys(this.subscriptions)) {
      this.subscriptions[key]?.unsubscribe();
      this.subscriptions[key] = undefined;
    }
  }
}
