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
import type { Subscription } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportDocumentListItemDto,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackPricingBasis,
  BenchmarkReportPackRequest,
  SameProviderWarningDto
} from '../../../services/admin-benchmark.service';
import { AdminService, SystemAiConfigDto } from '../../../services/admin.service';
import { ModelPickerComponent, ModelPickerKey, ModelPickerOption, toModelPickerOptions } from '../../../shared/model-picker/model-picker.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import { REPORT_PACK_WRITER_ADVICE, REPORT_WRITER_ADVICE_LEAD } from '../run-ai-reports/report-writer-advice';
import type {
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis
} from '../model-comparison/model-comparison.models';
import {
  ReportDocumentLibraryComponent,
  ReportDocumentLibraryScope,
  disclosureLabel,
  formatCostUsd,
  formatUtc,
  statusLabel
} from './report-document-library.component';

export { disclosureLabel, formatCostUsd, formatUtc, statusLabel };

/** What the Report Pack dialog is opened on: the comparison request, its entries and its suite. */
export interface ReportPackContext {
  readonly runIds: readonly number[];
  readonly groupIds: readonly number[];
  readonly pricingBasis: BenchmarkModelComparisonPricingBasis;
  /** Every entry of the comparison; the non-Excluded ones are the possible subjects. */
  readonly entries: readonly BenchmarkModelComparisonEntryDto[];
  /**
   * Every entry's key (`run:<id>`, `group:<id>`), Excluded ones included: the set the server's
   * comparison key is computed from, the same runs and groups the request carries.
   */
  readonly entryKeys: readonly string[];
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

/** One stage of the job's rail: Queued, Preparing, one per document, Done. */
export interface ReportPackJobStage {
  readonly key: string;
  readonly name: string;
  readonly state: 'done' | 'current' | 'pending';
}

/** The estimate panel: waiting, failed, no price for the writer, or the total with its parts. */
export interface ReportPackEstimateView {
  readonly state: 'loading' | 'failed' | 'noPrice' | 'ready';
  readonly total: string | null;
  /** One entry per document, only when there are two or more. */
  readonly parts: readonly { name: string; cost: string }[];
}

export const REPORT_PACK_AUDIENCES: readonly ReportPackAudienceOption[] = [
  {
    audience: BenchmarkReportAudience.ExecutiveSummary,
    label: 'Executive Summary',
    description: 'For a non-specialist at the model’s provider, or a manager: plain language, short.',
    checkedByDefault: true
  },
  {
    audience: BenchmarkReportAudience.TechnicalReport,
    label: 'Report for AI Researchers and Developers',
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

/** The wait after the last change of subject, documents or writer before the estimate is requested. */
export const REPORT_PACK_PREVIEW_DEBOUNCE_MS = 400;

/** The interval between two progress requests while a job runs. */
export const REPORT_PACK_POLL_MS = 2000;

/** The longest wait between progress requests after repeated failures. */
export const REPORT_PACK_POLL_MAX_BACKOFF_MS = 30000;

/** The per-viewer memory of the last writer used and of the sidebar's width. */
export const REPORT_PACK_STORAGE_KEY = 'overseer.benchmark.reportPack';

/** The job statuses after which nothing changes. */
const FINISHED_JOB_STATUSES = new Set(['Completed', 'CompletedWithErrors', 'Canceled', 'Failed']);

/** A document's statuses once it is written or given up. */
const FINISHED_DOCUMENT_STATUSES = new Set(['Completed', 'CompletedWithWarnings', 'Failed', 'Canceled']);

/** A document's statuses while the writer works on it. */
const ACTIVE_DOCUMENT_STATUSES = new Set(['Writing', 'Repairing']);

export function audienceLabel(audience: BenchmarkReportAudience): string {
  return REPORT_PACK_AUDIENCES.find(option => option.audience === audience)?.label ?? `Document ${audience}`;
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

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
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

/** The dialog's stored record: the last writer used and the sidebar's width. */
interface StoredReportPackSettings {
  writerConfigId?: number;
  sidebarWidth?: number;
}

function readStoredSettings(): StoredReportPackSettings {
  try {
    const raw = localStorage.getItem(REPORT_PACK_STORAGE_KEY);
    if (!raw) {
      return {};
    }
    const parsed = JSON.parse(raw) as { writerConfigId?: unknown; sidebarWidth?: unknown } | null;
    const settings: StoredReportPackSettings = {};
    if (typeof parsed?.writerConfigId === 'number') {
      settings.writerConfigId = parsed.writerConfigId;
    }
    if (typeof parsed?.sidebarWidth === 'number' && Number.isFinite(parsed.sidebarWidth)) {
      settings.sidebarWidth = parsed.sidebarWidth;
    }
    return settings;
  } catch {
    return {};
  }
}

function writeStoredSettings(patch: StoredReportPackSettings): void {
  try {
    localStorage.setItem(REPORT_PACK_STORAGE_KEY, JSON.stringify({ ...readStoredSettings(), ...patch }));
  } catch {
    // Storage unavailable: the choice is simply not remembered.
  }
}

/**
 * The Report Pack dialog, full-screen over the Model Comparison wizard: the sidebar starts a report
 * pack for one entry of the comparison; the main area follows its job and lists the documents written
 * for this comparison, with view, download and delete (the report document library).
 *
 * `open(context)` shows it; `closed` fires however it closes. The dialog is a DOM descendant of the
 * wizard's `<dialog>`, so it stops its own close and cancel events, and those of the dialogs nested in
 * it, from reaching the wizard.
 */
@Component({
  selector: 'app-benchmark-report-pack-dialog',
  standalone: true,
  imports: [RunReportFrameComponent, ModelPickerComponent, InfoTipComponent, ReportDocumentLibraryComponent],
  templateUrl: './benchmark-report-pack-dialog.component.html',
  styleUrls: ['./benchmark-report-pack-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkReportPackDialogComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly adminService = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('reportPackDialog') dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('reportPackHeading') heading?: ElementRef<HTMLElement>;
  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('generateButton') generateButton?: ElementRef<HTMLButtonElement>;
  @ViewChild(ReportDocumentLibraryComponent) library?: ReportDocumentLibraryComponent;

  /** Emitted when the dialog closes, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();

  /** The writer field's *How the graders work* link: the host opens the guide at *Choosing grader models*. */
  @Output() readonly graderGuideRequested = new EventEmitter<void>();

  readonly audiences = REPORT_PACK_AUDIENCES;
  readonly audienceLabel = audienceLabel;
  readonly statusLabel = statusLabel;
  readonly formatCostUsd = formatCostUsd;
  readonly formatUtc = formatUtc;
  readonly writerAdvice = REPORT_PACK_WRITER_ADVICE;
  readonly writerAdviceLead = REPORT_WRITER_ADVICE_LEAD;
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
  /** The same-provider confirmation's sentence. */
  confirmWarningText = '';

  starting = false;
  startError: string | null = null;

  /** The job this dialog follows, and its last known state. */
  activeJobId: string | null = null;
  job: BenchmarkReportPackJobDto | null = null;
  /** When `job` was last read, for the elapsed time of a running job. */
  jobReadAt = 0;
  /** The estimate when the job was started here, for its stat strip. */
  jobEstimateUsd: number | null = null;
  pollError: string | null = null;
  canceling = false;

  // --- Documents ---
  /** What the library lists; set on open, so its identity changes only with the comparison. */
  libraryScope: ReportDocumentLibraryScope | null = null;
  /** Bumped to make the library list its documents again. */
  libraryReloadToken = 0;
  /** How many documents the library holds. */
  libraryDocumentCount = 0;

  /** The sidebar's width in CSS px, as last stored; null for the frame's default. */
  sidebarWidth: number | null = readStoredSettings().sidebarWidth ?? null;

  /** Bumped on every open and close, so a response for an earlier opening never lands. */
  private generation = 0;
  private previewTimer: ReturnType<typeof setTimeout> | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollFailures = 0;
  /** The job whose finish already refreshed the documents list. */
  private refreshedForJobId: string | null = null;
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
    this.confirmWarningText = '';
    this.starting = false;
    this.startError = null;
    this.activeJobId = null;
    this.job = null;
    this.jobEstimateUsd = null;
    this.pollError = null;
    this.pollFailures = 0;
    this.canceling = false;
    this.refreshedForJobId = null;
    this.libraryScope = { kind: 'comparison', entryKeys: [...context.entryKeys] };
    this.libraryReloadToken++;
    this.libraryDocumentCount = 0;
    this.sidebarWidth = readStoredSettings().sidebarWidth ?? null;

    const generation = this.generation;
    this.loadWriters(generation);
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

  /** A nested dialog's close or cancel event, stopped so it reaches neither this dialog nor the wizard. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  /** `<suite> · N models · M possible subjects`. */
  get subtitle(): string {
    const suite = this.context?.suiteName || 'Suite not set';
    const models = this.context?.entries.length ?? 0;
    const count = this.subjects.length;
    return `${suite} · ${plural(models, 'model', 'models')} · ${count} possible ${count === 1 ? 'subject' : 'subjects'}`;
  }

  /** The line under *Documents of this comparison*: which documents it lists. */
  get libraryScopeLine(): string {
    const models = this.context?.entries.length ?? 0;
    return `Reports whose subject is one of the ${plural(models, 'model', 'models')} of this comparison, `
      + 'written for this same set of models.';
  }

  requestGraderGuide(): void {
    this.graderGuideRequested.emit();
  }

  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    writeStoredSettings({ sidebarWidth: width });
    this.cdr.markForCheck();
  }

  // -------------------------------------------------------------------------------------------
  // The form: subject, documents, writer
  // -------------------------------------------------------------------------------------------

  selectSubject(key: string): void {
    if (key === this.subjectKey) {
      return;
    }
    this.subjectKey = key;
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
    this.serverWarning = null;
    this.startError = null;
    this.schedulePreview();
  }

  get selectedWriter(): SystemAiConfigDto | null {
    return this.writers.find(writer => writer.id === this.writerId) ?? null;
  }

  /** The preview's same-provider warning, or the one a 409 carried. Generate then asks first. */
  get sameProviderWarning(): string | null {
    if (this.preview?.refusal) {
      return null;
    }
    return this.preview?.sameProviderWarning ?? this.serverWarning;
  }

  /** The picker's description: the refusal or the warning while there is one. */
  get writerDescribedBy(): string | null {
    const ids = [this.preview?.refusal ? 'rp-writer-refusal' : '', this.sameProviderWarning ? 'rp-same-provider-text' : '']
      .filter(id => id !== '');
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /** The request body, or null while the form is incomplete. */
  buildRequest(acknowledgeSameProvider = false): BenchmarkReportPackRequest | null {
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
      acknowledgeSameProvider
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

  /** The *Estimated cost* panel, or null while there is nothing to say. */
  get estimateView(): ReportPackEstimateView | null {
    if (this.previewPending) {
      return { state: 'loading', total: null, parts: [] };
    }
    if (this.previewError) {
      return { state: 'failed', total: null, parts: [] };
    }
    const preview = this.preview;
    if (!preview || preview.refusal) {
      return null;
    }
    const total = preview.estimatedTotalCostUsd;
    if (total === null || total === undefined) {
      return { state: 'noPrice', total: null, parts: [] };
    }
    const parts = preview.estimates.length > 1
      ? preview.estimates.map(estimate => ({ name: audienceLabel(estimate.audience), cost: formatCostUsd(estimate.estimatedCostUsd) }))
      : [];
    return { state: 'ready', total: formatCostUsd(total), parts };
  }

  /** The estimate's subject and peers, under the total. */
  get estimateSubjectLine(): string {
    const preview = this.preview;
    if (!preview) {
      return '';
    }
    return `For ${preview.subjectLabel} against ${plural(preview.peers.length, 'peer', 'peers')}. `
      + 'A repair turn can roughly double a document’s cost.';
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
    return null;
  }

  /** The Generate button's description: the estimate, and the reason it is blocked while it is. */
  get generateDescribedBy(): string {
    return this.generateBlockedReason !== null ? 'rp-estimate rp-generate-blocked' : 'rp-estimate';
  }

  /** Generate: a same-provider writer asks first, on every generate; any other writer starts at once. */
  generate(): void {
    if (this.buildRequest() === null || this.generateBlockedReason !== null) {
      return;
    }
    const warning = this.sameProviderWarning;
    if (warning) {
      this.openSameProviderConfirm(warning);
      return;
    }
    this.startPack(false);
  }

  confirmSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.startPack(true);
  }

  cancelSameProvider(): void {
    this.sameProviderDialog?.nativeElement.close();
    this.generateButton?.nativeElement.focus();
  }

  private openSameProviderConfirm(text: string): void {
    this.confirmWarningText = text;
    this.cdr.detectChanges();
    const dialog = this.sameProviderDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  private startPack(acknowledgeSameProvider: boolean): void {
    const request = this.buildRequest(acknowledgeSameProvider);
    if (!request || this.starting || this.jobInProgress) {
      return;
    }
    this.starting = true;
    this.startError = null;
    this.pollError = null;
    const estimateUsd = this.preview?.estimatedTotalCostUsd ?? null;
    writeStoredSettings({ writerConfigId: request.writerModelConfigurationId });
    const generation = this.generation;
    this.subscriptions['start'] = this.benchmarkService.startReportPack(request).subscribe({
      next: response => {
        if (generation !== this.generation) {
          return;
        }
        this.starting = false;
        this.activeJobId = response.jobId;
        this.job = null;
        this.jobEstimateUsd = estimateUsd;
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
        const body = error.error;
        // The server saw a same-provider writer the preview did not: ask, as for the preview's.
        if (error.status === 409 && !acknowledgeSameProvider && !isJobDto(body) && isSameProviderWarning(body)) {
          this.serverWarning = body.message;
          this.cdr.markForCheck();
          this.openSameProviderConfirm(body.message);
          return;
        }
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
        + 'One job runs at a time; its progress is shown in Documents of this comparison.';
    }
    if (error.status === 409 && isSameProviderWarning(body)) {
      this.serverWarning = body.message;
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

  /** The job has finished; its card shows a one-line summary. */
  get jobFinished(): boolean {
    return this.job !== null && FINISHED_JOB_STATUSES.has(this.job.status);
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

  /** The live line: announced as the job advances from phase to phase. */
  get jobStatusLine(): string {
    const job = this.job;
    if (!job) {
      return this.activeJobId ? 'Starting the report pack…' : '';
    }
    const total = job.documents.length;
    const finished = job.documents.filter(d => FINISHED_DOCUMENT_STATUSES.has(d.status)).length;
    if (job.status === 'Running') {
      return `Writing ${total} ${total === 1 ? 'document' : 'documents'} for ${job.subjectLabel}: ${finished} of ${total} finished.`;
    }
    return `Report pack for ${job.subjectLabel}: ${statusLabel(job.status)}.`;
  }

  /** Queued, Preparing, one stage per document, Done; the current one is where the job stands. */
  get jobStages(): ReportPackJobStage[] {
    const job = this.job;
    const documents = job?.documents ?? [];
    const stages = [
      { key: 'queued', name: 'Queued' },
      { key: 'preparing', name: 'Preparing' },
      ...documents.map(doc => ({ key: `doc-${doc.audience}`, name: audienceLabel(doc.audience) })),
      { key: 'done', name: 'Done' }
    ];
    let current = 0;
    if (job && FINISHED_JOB_STATUSES.has(job.status)) {
      current = stages.length;
    } else if (job) {
      const started = job.totalModelCalls > 0 || documents.some(doc => doc.status !== 'Pending');
      if (!started) {
        current = 1;
      } else {
        let index = documents.findIndex(doc => ACTIVE_DOCUMENT_STATUSES.has(doc.status));
        if (index < 0) {
          index = documents.findIndex(doc => doc.status === 'Pending');
        }
        if (index < 0) {
          index = Math.max(documents.length - 1, 0);
        }
        current = 2 + index;
      }
    }
    return stages.map((stage, i): ReportPackJobStage => ({
      ...stage,
      state: i < current ? 'done' : i === current ? 'current' : 'pending'
    }));
  }

  get jobTokens(): string {
    const job = this.job;
    if (!job) {
      return '—';
    }
    return `${job.inputTokens.toLocaleString('en-US')} in · ${job.outputTokens.toLocaleString('en-US')} out`;
  }

  get jobCostLabel(): string {
    return this.jobFinished ? 'Cost' : 'Cost so far';
  }

  get jobEstimate(): string | null {
    return this.jobEstimateUsd === null ? null : `about ${formatCostUsd(this.jobEstimateUsd)}`;
  }

  /** The finished job in one line: `Written: 2 documents, 1 min 12 s, $0.21`. */
  get jobSummary(): string {
    const job = this.job;
    if (!job) {
      return '';
    }
    const total = job.documents.length;
    const written = job.documents.filter(d => d.status === 'Completed' || d.status === 'CompletedWithWarnings').length;
    const tail = [this.jobElapsed, formatCostUsd(job.costUsd)].filter(part => part !== '').join(', ');
    let head: string;
    switch (job.status) {
      case 'Completed':
        head = `Written: ${plural(written, 'document', 'documents')}`;
        break;
      case 'CompletedWithErrors':
        head = `Written with errors: ${written} of ${plural(total, 'document', 'documents')}`;
        break;
      case 'Canceled':
        head = `Canceled: ${written} of ${plural(total, 'document', 'documents')} written`;
        break;
      default:
        head = `Failed: ${written} of ${plural(total, 'document', 'documents')} written`;
    }
    return tail ? `${head}, ${tail}` : head;
  }

  /** Dismiss: the finished job's card goes away. */
  dismissJob(): void {
    if (this.jobInProgress) {
      return;
    }
    this.activeJobId = null;
    this.job = null;
    this.jobEstimateUsd = null;
    this.pollError = null;
    this.cdr.markForCheck();
    document.getElementById('rp-documents-heading')?.focus();
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
        this.libraryReloadToken++;
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
        const remembered = readStoredSettings().writerConfigId ?? null;
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

  onLibraryDocuments(documents: readonly BenchmarkReportDocumentListItemDto[]): void {
    this.libraryDocumentCount = documents.length;
    this.cdr.markForCheck();
  }

  /** The *Downloads* notice's Open Download Center: every document of the comparison. */
  openAllDownloads(button: HTMLElement): void {
    this.library?.openDownloadCenterForAll(button);
  }

  // -------------------------------------------------------------------------------------------
  // Housekeeping
  // -------------------------------------------------------------------------------------------

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
