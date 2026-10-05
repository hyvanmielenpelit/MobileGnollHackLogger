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
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, interval } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkReportAudience,
  BenchmarkReportPackDocumentProgressDto,
  BenchmarkReportPackJobDto,
  BenchmarkReportPackPreviewDto,
  BenchmarkReportPackPricingBasis,
  BenchmarkReportPackRequest,
  BenchmarkReportPackWrittenDocumentDto,
  SameProviderWarningDto
} from '../../../services/admin-benchmark.service';
import { AdminService, SystemAiConfigDto } from '../../../services/admin.service';
import { ModelPickerComponent, ModelPickerKey, ModelPickerOption, toModelPickerOptions } from '../../../shared/model-picker/model-picker.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import { REPORT_PACK_WRITER_ADVICE, REPORT_WRITER_ADVICE_LEAD } from '../run-ai-reports/report-writer-advice';
import type { ClientPollError } from '../run-ai-reports/run-report-writing-diagnostics';
import { formatUtcSeconds } from '../run-ai-reports/run-report-writing-diagnostics';
import type {
  BenchmarkModelComparisonEntryDto,
  BenchmarkModelComparisonPricingBasis
} from '../model-comparison/model-comparison.models';
import { ReportChartPickerComponent } from './report-chart-picker.component';
import { ReportChartRowStatus, ReportChartSelection } from './report-charts';
import {
  REPORT_PACK_AUDIENCES,
  audienceLabel,
  documentChipClass,
  formatCostUsd,
  formatElapsed,
  formatUtc,
  statusLabel
} from './report-document-format';
import { buildReportPackDiagnostics, reportPackDiagnosticsFileName, reportPackIo } from './report-pack-diagnostics';

/** What the Reports step works on: the comparison request, its entries and its suite. */
export interface ReportPackContext {
  readonly runIds: readonly number[];
  readonly groupIds: readonly number[];
  /** The battery results of a comparison of battery results; never beside runs or groups. Absent reads as none. */
  readonly batteryRunIds?: readonly number[];
  readonly pricingBasis: BenchmarkModelComparisonPricingBasis;
  /** Every entry of the comparison; the non-Excluded ones are the possible subjects. */
  readonly entries: readonly BenchmarkModelComparisonEntryDto[];
  /**
   * Every entry's key (`run:<id>`, `group:<id>`, `battery:<id>`), Excluded ones included: the set the
   * server's comparison key is computed from, the same sources the request carries.
   */
  readonly entryKeys: readonly string[];
  readonly suiteId: number | null;
  readonly suiteName: string | null;
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

/** The charts cell of a document's progress row: its words, which are the retry button's after a failure. */
export interface ReportPackChartCell {
  readonly text: string;
  readonly retry: boolean;
}

/** One row of the document progress list. */
export interface ReportPackDocumentRow {
  readonly doc: BenchmarkReportPackDocumentProgressDto;
  readonly audience: BenchmarkReportAudience;
  readonly name: string;
  readonly statusWord: string;
  readonly chipClass: string;
  readonly duration: string;
  readonly modelCalls: string;
  readonly errorMessage: string | null;
  readonly charts: ReportPackChartCell;
}

/** The wait after the last change of subject, documents or writer before the estimate is requested. */
export const REPORT_PACK_PREVIEW_DEBOUNCE_MS = 400;

/** The interval between two progress requests while a job runs. */
export const REPORT_PACK_POLL_MS = 2000;

/** The longest wait between progress requests after repeated failures. */
export const REPORT_PACK_POLL_MAX_BACKOFF_MS = 30000;

/** The per-viewer memory of the last writer used and of the sidebar's width. */
export const REPORT_PACK_STORAGE_KEY = 'overseer.benchmark.reportPack';

/** The warning shown when the server has no chart folder. */
export const REPORT_PACK_CHART_STORAGE_MISSING_TEXT =
  'Chart storage is not configured; documents will be written without charts.';

/** The paragraph under the step heading: what these reports are, and where a run's own reports are written. */
export const REPORT_PACK_LEAD_TEXT =
  'These reports compare the chosen model with every other model of this comparison and are kept with the '
  + 'comparison: step 4 lists them, and so does Comparison reports on the Model Comparison tab. A run\'s or '
  + 'battery run\'s own reports are written in the AI Reports tab of its report.';

/** The server's refusal of a subject with no peer, word for word (`BenchmarkReportPackPreparation.PeerlessReportRefusal`). */
export const REPORT_PACK_PEERLESS_REFUSAL =
  'A comparison report compares one model with at least one other. To write a run\'s or a battery run\'s own '
  + 'reports, use the AI Reports tab of its report.';

/** Why Generate is unavailable when every document about the subject is already written. */
export const REPORT_PACK_ALL_WRITTEN_REASON =
  'Every document about this subject is already written for this comparison. Delete one in step 4 to write it again.';

/** The job statuses after which nothing changes. */
const FINISHED_JOB_STATUSES = new Set(['Completed', 'CompletedWithErrors', 'Canceled', 'Failed']);

/** A document's statuses once it is written or given up. */
const FINISHED_DOCUMENT_STATUSES = new Set(['Completed', 'CompletedWithWarnings', 'Failed', 'Canceled']);

/** A document's statuses once it is written. */
const WRITTEN_DOCUMENT_STATUSES = new Set(['Completed', 'CompletedWithWarnings']);

/** A document's statuses while the writer works on it. */
const ACTIVE_DOCUMENT_STATUSES = new Set(['Writing', 'Repairing']);

const numberFormat = new Intl.NumberFormat('en-US');

/** An entry's option text, with the axes a Degraded entry is degraded on. */
export function subjectOptionLabel(entry: BenchmarkModelComparisonEntryDto): string {
  if (entry.state !== 'Degraded' && !entry.speedDegraded && !entry.costDegraded) {
    return entry.label;
  }
  const axes = [entry.speedDegraded ? 'speed' : null, entry.costDegraded ? 'cost' : null]
    .filter((axis): axis is string => axis !== null);
  return axes.length > 0 ? `${entry.label} (${axes.join(' and ')} degraded)` : `${entry.label} (degraded)`;
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

/** The panel's stored record: the last writer used and the sidebar's width. */
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
 * Step 3 of the Model Comparison wizard, *Reports*: a sidebar that starts a report pack for one
 * entry of the comparison, with the charts each document carries; and a main area that follows the
 * job, one row per document, and its log and diagnostics. The written documents are step 4's.
 *
 * The wizard creates it once and hides it while another step shows, so the job's polling and the
 * one-second clock go on while it is hidden; both stop in `ngOnDestroy`. The form resets only when
 * the comparison's entry keys change, and then the running job, if any, is found again.
 */
@Component({
  selector: 'app-report-pack-panel',
  standalone: true,
  imports: [RunReportFrameComponent, ModelPickerComponent, InfoTipComponent, ReportChartPickerComponent],
  templateUrl: './report-pack-panel.component.html',
  styleUrls: ['./report-pack-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ReportPackPanelComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly adminService = inject(AdminService);
  private readonly cdr = inject(ChangeDetectorRef);

  @ViewChild('sameProviderDialog') sameProviderDialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('generateButton') generateButton?: ElementRef<HTMLButtonElement>;

  /** The comparison; the form resets only when its entry keys change. */
  @Input()
  set context(value: ReportPackContext | null) {
    this.applyContext(value ?? null);
  }
  get context(): ReportPackContext | null {
    return this.currentContext;
  }

  /** The charts each document type carries; the wizard stores every change. */
  @Input() chartSelection: ReportChartSelection = {};

  /** The figure keys the comparison can draw. */
  @Input() chartsAvailable: readonly string[] = [];

  /** Why the charts may not match what the documents say (D10), or null. */
  @Input() chartAdvisory: string | null = null;

  /** The server has no chart folder: documents are written without charts. */
  @Input() chartStorageMissing = false;

  /** Each written document's chart state, keyed by document id. */
  @Input() chartStatus: Readonly<Record<number, ReportChartRowStatus>> = {};

  /** The prefix of every id in the panel, so the wizard can label its step region by `${idPrefix}-heading`. */
  @Input() idPrefix = 'rp';

  /** The writer field's *How the graders work* link: the host opens the guide at *Choosing grader models*. */
  @Output() readonly graderGuideRequested = new EventEmitter<void>();

  /** A document reached Completed or Completed with warnings; once per document id. */
  @Output() readonly documentWritten = new EventEmitter<BenchmarkReportPackDocumentProgressDto>();

  /** The job followed here finished; once per job. */
  @Output() readonly jobFinished = new EventEmitter<BenchmarkReportPackJobDto>();

  /** *See the documents* after a job: the host shows step 4. */
  @Output() readonly documentsRequested = new EventEmitter<void>();

  /** The chart picker's change, normalized. */
  @Output() readonly chartSelectionChange = new EventEmitter<ReportChartSelection>();

  /** *Charts failed — retry* on a document's row. */
  @Output() readonly chartRetryRequested = new EventEmitter<BenchmarkReportPackDocumentProgressDto>();

  /** Whether a job is running, emitted on every change. */
  @Output() readonly busyChange = new EventEmitter<boolean>();

  readonly audiences = REPORT_PACK_AUDIENCES;
  readonly audienceLabel = audienceLabel;
  readonly statusLabel = statusLabel;
  readonly formatCostUsd = formatCostUsd;
  readonly formatUtc = formatUtc;
  readonly writerAdvice = REPORT_PACK_WRITER_ADVICE;
  readonly writerAdviceLead = REPORT_WRITER_ADVICE_LEAD;
  readonly chartStorageMissingText = REPORT_PACK_CHART_STORAGE_MISSING_TEXT;
  readonly leadText = REPORT_PACK_LEAD_TEXT;
  readonly writerEmptyHint =
    'No system AI configs with the Benchmark role are enabled. Enable the Benchmark role in System Configs.';

  private currentContext: ReportPackContext | null = null;
  /** The entry keys the form was last reset for. */
  private contextKey: string | null = null;

  // --- New report pack ---
  subjects: ReportPackSubjectOption[] = [];
  subjectKey: string | null = null;
  private readonly checkedAudiences = new Set<BenchmarkReportAudience>();
  /** The checked documents, in the fixed audience order; replaced on every change, for the chart picker. */
  selectedAudiences: BenchmarkReportAudience[] = [];
  /**
   * The documents already written for this comparison, per subject key, as the last estimate for that
   * subject listed them. Such a document's audience cannot be checked.
   */
  private readonly writtenBySubject = new Map<string, readonly BenchmarkReportPackWrittenDocumentDto[]>();

  writers: SystemAiConfigDto[] = [];
  writersLoading = false;
  writersError: string | null = null;
  writerId: number | null = null;
  private writersLoaded = false;
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

  /** The job this panel follows, and its last known state. */
  activeJobId: string | null = null;
  job: BenchmarkReportPackJobDto | null = null;
  /** When `job` was last read (client clock), to run the server's clock on between readings. */
  jobReadAt = 0;
  /** The estimate when the job was started here, for its stat strip. */
  jobEstimateUsd: number | null = null;
  pollError: string | null = null;
  canceling = false;

  // --- Client polling, for the diagnostics ---
  pollCount = 0;
  lastSuccessUtc: Date | null = null;
  lastPollError: ClientPollError | null = null;

  copyStatus = '';
  copyError: string | null = null;

  /** The sidebar's width in CSS px, as last stored; null for the frame's default. */
  sidebarWidth: number | null = readStoredSettings().sidebarWidth ?? null;

  /** Bumped on every context reset and on destroy, so a response for an earlier comparison never lands. */
  private generation = 0;
  private previewTimer: ReturnType<typeof setTimeout> | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollFailures = 0;
  private tickSub: Subscription | null = null;
  private writersSub: Subscription | null = null;
  private destroyed = false;
  private lastBusy = false;
  /** The documents already announced by `documentWritten`, and the jobs by `jobFinished`. */
  private readonly writtenDocumentIds = new Set<number>();
  private readonly finishedJobIds = new Set<string>();
  private readonly subscriptions: Record<string, Subscription | undefined> = {};

  ngOnInit(): void {
    ensureOverlayPolyfills();
    this.loadWriters();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.generation++;
    this.stopTimers();
    this.stopTick();
    this.writersSub?.unsubscribe();
    this.writersSub = null;
  }

  // -------------------------------------------------------------------------------------------
  // The comparison
  // -------------------------------------------------------------------------------------------

  /**
   * A new comparison resets the form (first non-Excluded entry as the subject, the default
   * documents, the remembered writer) and looks for a running job; the same entry keys only update
   * the request's facts, keeping the form and the job.
   */
  private applyContext(context: ReportPackContext | null): void {
    const key = context ? context.entryKeys.join(',') : null;
    const previous = this.currentContext;
    this.currentContext = context;
    if (this.destroyed) {
      return;
    }
    if (key === this.contextKey) {
      if (context && previous) {
        this.refreshSubjects(context);
        if (context.pricingBasis !== previous.pricingBasis && !this.jobInProgress) {
          this.schedulePreview();
        }
      }
      this.cdr.markForCheck();
      return;
    }
    this.contextKey = key;
    this.resetForm(context);
  }

  private refreshSubjects(context: ReportPackContext): void {
    this.subjects = context.entries
      .filter(entry => !entry.excluded && entry.state !== 'Excluded')
      .map(entry => ({ key: entry.key, label: subjectOptionLabel(entry) }));
    if (this.subjectKey === null || !this.subjects.some(subject => subject.key === this.subjectKey)) {
      this.subjectKey = this.subjects[0]?.key ?? null;
    }
  }

  private resetForm(context: ReportPackContext | null): void {
    this.generation++;
    this.stopTimers();
    this.stopTick();
    this.subjects = [];
    this.subjectKey = null;
    if (context) {
      this.refreshSubjects(context);
    }
    this.writtenBySubject.clear();
    this.checkedAudiences.clear();
    for (const option of REPORT_PACK_AUDIENCES) {
      if (option.checkedByDefault) {
        this.checkedAudiences.add(option.audience);
      }
    }
    this.syncSelectedAudiences();
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
    this.pollCount = 0;
    this.lastSuccessUtc = null;
    this.lastPollError = null;
    this.canceling = false;
    this.copyStatus = '';
    this.copyError = null;
    this.sidebarWidth = readStoredSettings().sidebarWidth ?? null;
    // Reached from an input setter, inside the host's change detection: the event waits for it to end.
    this.syncRunning(true);

    if (context) {
      this.applyRememberedWriter();
      this.loadActiveJob(this.generation);
    }
    this.cdr.markForCheck();
  }

  /** `<suite> · N models · M possible subjects`. */
  get subtitle(): string {
    const suite = this.currentContext?.suiteName || 'Suite not set';
    const models = this.currentContext?.entries.length ?? 0;
    const count = this.subjects.length;
    return `${suite} · ${plural(models, 'model', 'models')} · ${count} possible ${count === 1 ? 'subject' : 'subjects'}`;
  }

  get headingId(): string {
    return `${this.idPrefix}-heading`;
  }

  requestGraderGuide(): void {
    this.graderGuideRequested.emit();
  }

  requestDocuments(): void {
    this.documentsRequested.emit();
  }

  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    writeStoredSettings({ sidebarWidth: width });
    this.cdr.markForCheck();
  }

  /** A nested dialog's close or cancel event, stopped so it never reaches the wizard's dialog. */
  stopNestedEvent(event: Event): void {
    event.stopPropagation();
  }

  // -------------------------------------------------------------------------------------------
  // The form: subject, documents, charts, writer
  // -------------------------------------------------------------------------------------------

  selectSubject(key: string): void {
    if (key === this.subjectKey) {
      return;
    }
    this.subjectKey = key;
    this.serverWarning = null;
    this.startError = null;
    this.uncheckWritten();
    this.schedulePreview();
  }

  onSubjectChange(event: Event): void {
    this.selectSubject((event.target as HTMLSelectElement).value);
  }

  isAudienceChecked(audience: BenchmarkReportAudience): boolean {
    return this.checkedAudiences.has(audience);
  }

  /** The subject's document of this audience already written for this comparison, or null. */
  writtenDocument(audience: BenchmarkReportAudience): BenchmarkReportPackWrittenDocumentDto | null {
    if (this.subjectKey === null) {
      return null;
    }
    return this.writtenBySubject.get(this.subjectKey)?.find(doc => doc.audience === audience) ?? null;
  }

  isAudienceWritten(audience: BenchmarkReportAudience): boolean {
    return this.writtenDocument(audience) !== null;
  }

  /** `Written <yyyy-MM-dd HH:mm> UTC by <writer>. Delete it in step 4 to write it again.`, or '' when not written. */
  writtenHint(audience: BenchmarkReportAudience): string {
    const doc = this.writtenDocument(audience);
    if (!doc) {
      return '';
    }
    return `Written ${formatUtc(doc.createdAtUtc)} by ${doc.writerDisplayName || 'an unknown writer'}. `
      + 'Delete it in step 4 to write it again.';
  }

  /** The audience checkbox's description: what the document is, and when it was written if it was. */
  audienceDescribedBy(audience: BenchmarkReportAudience): string {
    const base = `${this.idPrefix}-audience-${audience}`;
    return this.isAudienceWritten(audience) ? `${base}-desc ${base}-written` : `${base}-desc`;
  }

  /** Every document about the subject is already written for this comparison. */
  get allAudiencesWritten(): boolean {
    return this.subjectKey !== null && REPORT_PACK_AUDIENCES.every(option => this.isAudienceWritten(option.audience));
  }

  setAudience(audience: BenchmarkReportAudience, checked: boolean): void {
    if (checked && this.isAudienceWritten(audience)) {
      return;
    }
    if (checked) {
      this.checkedAudiences.add(audience);
    } else {
      this.checkedAudiences.delete(audience);
    }
    this.syncSelectedAudiences();
    this.startError = null;
    this.schedulePreview();
  }

  onAudienceChange(audience: BenchmarkReportAudience, event: Event): void {
    this.setAudience(audience, (event.target as HTMLInputElement).checked);
  }

  private syncSelectedAudiences(): void {
    this.selectedAudiences = REPORT_PACK_AUDIENCES.map(option => option.audience)
      .filter(audience => this.checkedAudiences.has(audience));
  }

  /** Unchecks every checked document the subject already has; true when one was unchecked. */
  private uncheckWritten(): boolean {
    let changed = false;
    for (const audience of [...this.checkedAudiences]) {
      if (this.isAudienceWritten(audience)) {
        this.checkedAudiences.delete(audience);
        changed = true;
      }
    }
    if (changed) {
      this.syncSelectedAudiences();
    }
    return changed;
  }

  onChartSelectionChange(selection: ReportChartSelection): void {
    this.chartSelectionChange.emit(selection);
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
    const ids = [
      this.preview?.refusal ? `${this.idPrefix}-writer-refusal` : '',
      this.sameProviderWarning ? `${this.idPrefix}-same-provider-text` : ''
    ].filter(id => id !== '');
    return ids.length > 0 ? ids.join(' ') : null;
  }

  /**
   * The request body, or null while the form is incomplete. `batteryRunIds` is sent only for a
   * comparison of battery results, so a run comparison's request is unchanged by it.
   */
  buildRequest(acknowledgeSameProvider = false): BenchmarkReportPackRequest | null {
    const context = this.currentContext;
    if (!context || this.subjectKey === null || this.writerId === null) {
      return null;
    }
    const batteryRunIds = context.batteryRunIds ?? [];
    return {
      runIds: [...context.runIds],
      groupIds: [...context.groupIds],
      ...(batteryRunIds.length > 0 ? { batteryRunIds: [...batteryRunIds] } : {}),
      pricingBasis: context.pricingBasis === 'AsRun'
        ? BenchmarkReportPackPricingBasis.AsRun
        : BenchmarkReportPackPricingBasis.Current,
      subjectKey: this.subjectKey,
      audiences: [...this.selectedAudiences],
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
        this.writtenBySubject.set(request.subjectKey, preview.writtenDocuments ?? []);
        // A checked document that turns out written is unchecked, and the estimate asked again without it.
        if (request.subjectKey === this.subjectKey && this.uncheckWritten()) {
          this.schedulePreview();
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
    if (this.allAudiencesWritten) {
      return REPORT_PACK_ALL_WRITTEN_REASON;
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
    if (this.preview?.refusal === REPORT_PACK_PEERLESS_REFUSAL) {
      return 'This subject has no other model to be compared with in this comparison.';
    }
    if (this.preview?.refusal) {
      return 'This writer is refused for this subject. Choose another writer.';
    }
    return null;
  }

  /**
   * Generate stays focusable, `aria-disabled`, when every document is already written, so a keyboard
   * reader lands on it and hears why; every other block disables it.
   */
  get generateAriaDisabled(): boolean {
    return this.generateBlockedReason === REPORT_PACK_ALL_WRITTEN_REASON;
  }

  /** The Generate button's description: the estimate, and the reason it is blocked while it is. */
  get generateDescribedBy(): string {
    const estimate = `${this.idPrefix}-estimate`;
    return this.generateBlockedReason !== null ? `${estimate} ${this.idPrefix}-generate-blocked` : estimate;
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
    this.syncRunning();
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
        this.pollFailures = 0;
        this.syncRunning();
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
          this.syncRunning();
          this.cdr.markForCheck();
          this.openSameProviderConfirm(body.message);
          return;
        }
        this.startError = this.startErrorMessage(error);
        this.syncRunning();
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
        + 'One job runs at a time; its progress is shown here.';
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

  /** A job is running: the host keeps the wizard from closing on it unawares. */
  get jobRunning(): boolean {
    return this.jobInProgress;
  }

  /** The job has finished; its card shows a one-line summary. */
  get isJobFinished(): boolean {
    return this.job !== null && FINISHED_JOB_STATUSES.has(this.job.status);
  }

  get jobDocuments(): BenchmarkReportPackDocumentProgressDto[] {
    return this.job?.documents ?? [];
  }

  /**
   * The server's clock now: its last reading plus the client time since that response arrived;
   * the client's clock when the server sent none.
   */
  private serverNowMs(): number {
    const serverTime = this.job?.serverTimeUtc;
    if (serverTime) {
      const server = parseServerUtcDate(serverTime).getTime();
      if (!Number.isNaN(server)) {
        return server + Math.max(0, Date.now() - this.jobReadAt);
      }
    }
    return Date.now();
  }

  /** Milliseconds from `startUtc` to `endUtc`, or to the server's clock now while `endUtc` is absent. */
  private spanToNow(startUtc: string | null | undefined, endUtc: string | null | undefined): number | null {
    if (!startUtc) {
      return null;
    }
    const start = parseServerUtcDate(startUtc).getTime();
    const end = endUtc ? parseServerUtcDate(endUtc).getTime() : this.serverNowMs();
    if (Number.isNaN(start) || Number.isNaN(end)) {
      return null;
    }
    return Math.max(0, end - start);
  }

  get jobElapsed(): string {
    const job = this.job;
    if (!job) {
      return '';
    }
    const ms = this.spanToNow(job.startedAtUtc, job.completedAtUtc);
    return ms === null ? '' : formatElapsed(ms);
  }

  /** The live line: it changes as the job moves from phase to phase, never with the clock. */
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

  /** One row per document: its chip, its duration on the server's clock, its model calls and its charts. */
  get documentRows(): ReportPackDocumentRow[] {
    const job = this.job;
    if (!job) {
      return [];
    }
    const finished = FINISHED_JOB_STATUSES.has(job.status);
    return job.documents.map((doc): ReportPackDocumentRow => {
      const duration = doc.startedAtUtc
        ? this.spanToNow(doc.startedAtUtc, doc.completedAtUtc ?? (finished ? job.completedAtUtc : null))
        : null;
      return {
        doc,
        audience: doc.audience,
        name: audienceLabel(doc.audience),
        statusWord: statusLabel(doc.status),
        chipClass: documentChipClass(doc.status),
        duration: duration === null ? '—' : formatElapsed(duration),
        modelCalls: numberFormat.format(doc.modelCalls),
        errorMessage: doc.errorMessage || null,
        charts: this.chartCell(doc)
      };
    });
  }

  private chartCell(doc: BenchmarkReportPackDocumentProgressDto): ReportPackChartCell {
    if (this.chartStorageMissing || (this.chartSelection[doc.audience] ?? []).length === 0) {
      return { text: 'Charts: none', retry: false };
    }
    const status = doc.documentId === null ? undefined : this.chartStatus[doc.documentId];
    if (!status) {
      return { text: '—', retry: false };
    }
    switch (status.state) {
      case 'attaching':
        return { text: 'Charts: attaching…', retry: false };
      case 'done':
        return { text: `Charts: ${numberFormat.format(status.count)}`, retry: false };
      case 'failed':
        return { text: 'Charts failed — retry', retry: true };
      default:
        return { text: 'Charts: none', retry: false };
    }
  }

  retryCharts(row: ReportPackDocumentRow): void {
    this.chartRetryRequested.emit(row.doc);
  }

  get jobTokens(): string {
    const job = this.job;
    if (!job) {
      return '—';
    }
    return `${job.inputTokens.toLocaleString('en-US')} in · ${job.outputTokens.toLocaleString('en-US')} out`;
  }

  get jobCostLabel(): string {
    return this.isJobFinished ? 'Cost' : 'Cost so far';
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
    const written = job.documents.filter(d => WRITTEN_DOCUMENT_STATUSES.has(d.status)).length;
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
    this.syncRunning();
    this.cdr.markForCheck();
    document.getElementById(`${this.idPrefix}-progress-heading`)?.focus();
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
    this.pollFailures = 0;
    this.applyJob(job);
  }

  private applyJob(job: BenchmarkReportPackJobDto): void {
    this.job = job;
    this.jobReadAt = Date.now();
    this.pollError = null;
    for (const doc of job.documents) {
      if (doc.documentId !== null && WRITTEN_DOCUMENT_STATUSES.has(doc.status) && !this.writtenDocumentIds.has(doc.documentId)) {
        this.writtenDocumentIds.add(doc.documentId);
        this.documentWritten.emit(doc);
      }
    }
    if (FINISHED_JOB_STATUSES.has(job.status)) {
      this.clearPollTimer();
      if (!this.finishedJobIds.has(job.id)) {
        this.finishedJobIds.add(job.id);
        this.jobFinished.emit(job);
      }
    } else {
      this.schedulePoll(REPORT_PACK_POLL_MS);
    }
    this.syncRunning();
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
    this.pollCount++;
    this.subscriptions['poll'] = this.benchmarkService.getReportPackJob(id).subscribe({
      next: job => {
        if (generation !== this.generation) {
          return;
        }
        this.pollFailures = 0;
        this.lastSuccessUtc = new Date();
        this.applyJob(job);
        this.cdr.markForCheck();
      },
      error: (error: unknown) => {
        if (generation !== this.generation) {
          return;
        }
        this.pollFailures++;
        const http = error instanceof HttpErrorResponse ? error : null;
        this.lastPollError = {
          httpStatus: http && http.status > 0 ? http.status : null,
          message: (http ? serverMessage(http) ?? (http.statusText || http.message) : null) || 'Unknown error'
        };
        this.pollError = 'The job’s progress could not be read. Retrying.';
        this.schedulePoll(Math.min(REPORT_PACK_POLL_MS * 2 ** this.pollFailures, REPORT_PACK_POLL_MAX_BACKOFF_MS));
        this.cdr.markForCheck();
      }
    });
  }

  /** Keeps `busyChange` and the one-second clock in step with whether a job is running. */
  private syncRunning(deferEvent = false): void {
    const running = this.jobInProgress;
    if (running && this.activeJobId !== null) {
      if (!this.tickSub && !this.destroyed) {
        this.tickSub = interval(1000).subscribe(() => this.cdr.markForCheck());
      }
    } else {
      this.stopTick();
    }
    if (running !== this.lastBusy) {
      this.lastBusy = running;
      if (deferEvent) {
        void Promise.resolve().then(() => this.busyChange.emit(running));
      } else {
        this.busyChange.emit(running);
      }
    }
  }

  private stopTick(): void {
    this.tickSub?.unsubscribe();
    this.tickSub = null;
  }

  // -------------------------------------------------------------------------------------------
  // Writers
  // -------------------------------------------------------------------------------------------

  private loadWriters(): void {
    this.writersLoading = true;
    this.writersError = null;
    this.writersSub?.unsubscribe();
    this.writersSub = this.adminService.getSystemConfigs().subscribe({
      next: configs => {
        this.writersLoading = false;
        this.writersLoaded = true;
        this.writers = (configs ?? []).filter(c => (c.modelRole & 4) === 4 && c.hasApiKey && c.isEnabled);
        this.applyRememberedWriter();
        this.cdr.markForCheck();
      },
      error: () => {
        this.writersLoading = false;
        this.writersError = 'The report writers could not be loaded.';
        this.cdr.markForCheck();
      }
    });
  }

  /** The last writer used, while it still qualifies and no writer is chosen. */
  private applyRememberedWriter(): void {
    if (!this.writersLoaded || this.writerId !== null || this.currentContext === null) {
      return;
    }
    const remembered = readStoredSettings().writerConfigId ?? null;
    if (remembered !== null && this.writers.some(w => w.id === remembered)) {
      this.selectWriter(remembered);
    }
  }

  // -------------------------------------------------------------------------------------------
  // Diagnostics
  // -------------------------------------------------------------------------------------------

  diagnosticsText(nowUtc: Date = new Date()): string {
    return buildReportPackDiagnostics({
      context: this.currentContext,
      job: this.job,
      subjectKey: this.subjectKey,
      chartSelection: this.chartSelection,
      chartStatus: this.chartStatus,
      chartStorageMissing: this.chartStorageMissing,
      chartAdvisory: this.chartAdvisory,
      client: {
        pollCount: this.pollCount,
        lastSuccessUtc: this.lastSuccessUtc,
        consecutiveFailures: this.pollFailures,
        lastError: this.lastPollError
      },
      estimateUsd: this.jobEstimateUsd,
      nowUtc
    });
  }

  async copyDiagnostics(): Promise<void> {
    const generation = this.generation;
    this.copyStatus = '';
    this.copyError = null;
    this.cdr.markForCheck();
    const ok = await reportPackIo.copy(this.diagnosticsText());
    if (generation !== this.generation) {
      return;
    }
    if (ok) {
      this.copyStatus = 'Copied';
    } else {
      this.copyError = 'The diagnostics could not be copied to the clipboard. Download them instead.';
    }
    this.cdr.markForCheck();
  }

  downloadDiagnostics(): void {
    const now = new Date();
    const subject = this.job?.subjectLabel
      ?? this.subjects.find(subject => subject.key === this.subjectKey)?.label
      ?? null;
    reportPackIo.download(reportPackDiagnosticsFileName(subject, now), this.diagnosticsText(now));
  }

  logTime(timestampUtc: string): string {
    return formatUtcSeconds(timestampUtc) ?? timestampUtc;
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
