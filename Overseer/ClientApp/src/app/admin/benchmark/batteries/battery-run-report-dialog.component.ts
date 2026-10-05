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
import { firstValueFrom } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAnalysisDto,
  BenchmarkBatteryLeaderboardDto,
  BenchmarkBatteryLeaderboardRowDto,
  BenchmarkBatteryMemberDto,
  BenchmarkBatteryRunDto,
  BenchmarkBatteryRunSuiteDto,
  BenchmarkPairComparisonDto,
  BenchmarkRunDetailDto
} from '../../../services/admin-benchmark.service';
import { SystemService } from '../../../services/system.service';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { copyToClipboard } from '../../../utils/clipboard.util';
import { elapsedMsBetween, parseServerUtcDate } from '../../../utils/date.util';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../utils/polyfills.util';
import { batteryStatusBadgeClass, getScoreBadgeClass } from '../benchmark-run-format';
import { BenchmarkFingerprintEntry, COPY_STATUS_MS, FINGERPRINT_LONG_NAMES } from '../benchmark.models';
import { BenchmarkDownloadCenterComponent } from '../download-center/benchmark-download-center.component';
import { KeyFigureCardActionsComponent, KeyFigureCardExportRequest } from '../run-report-frame/key-figure-card-actions.component';
import { KeyFiguresChooserComponent, KeyFiguresImageMeasurer } from '../run-report-frame/key-figures-chooser.component';
import {
  KeyFiguresExportSettings,
  keyFiguresFormatLabel,
  readStoredKeyFiguresExportSettings,
  writeStoredKeyFiguresExportSettings
} from '../run-report-frame/key-figures-export-settings';
import {
  ImageContext,
  KeyFigureKey,
  KeyFiguresAction,
  exportKeyFiguresImage,
  measureKeyFiguresImage,
  readKeyFigureCells,
  readStoredImageDetailExclusions,
  readStoredKeyFigureExclusions,
  statusImageTone,
  storeImageDetailExclusions,
  storeKeyFigureExclusions,
  toImageFactRows
} from '../run-report-frame/key-figures-image';
import { RunFactsComponent } from '../run-report-frame/run-facts.component';
import {
  RUN_FACT_PRIMARY_KEYS,
  RunFactRow,
  buildBatteryRunFacts,
  runFactPlainText,
  runFactsReadout
} from '../run-report-frame/run-facts';
import { RunReportFrameComponent } from '../run-report-frame/run-report-frame.component';
import { VerdictShape, findPair, pairedErrorText, verdictShape, verdictText } from '../shared/paired-test/paired-test-format';
import { PairedTestResultComponent } from '../shared/paired-test/paired-test-result.component';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import { BatteryAiReportsComponent, BatteryReportStatusChange } from './battery-ai-reports.component';
import { BATTERY_SLOT_STATE_LABELS, BatterySlotState, batterySlotState } from './battery-progress-dialog.component';
import {
  BenchmarkBatteryComparison,
  BenchmarkBatteryOverallIndex,
  BenchmarkBatteryStatisticsResult,
  BenchmarkBatterySuiteProfile,
  batteryAwaitsPostRun,
  batteryRunStatusLabel,
  batterySchemeLabel,
  formatCost,
  formatIndexWithHalfWidth,
  formatInterval,
  formatMs,
  formatNumber,
  formatPercent,
  formatPValue,
  formatSigned,
  httpErrorText,
  isLiveBatteryRunStatus,
  randomizationMethodLabel,
  reproducibilitySourceLabel
} from './battery.models';

// -----------------------------------------------------------------------------------------------
// Tabs and remembered choices
// -----------------------------------------------------------------------------------------------

/** The Battery Run Report's tabs, in order. */
export const BATTERY_RUN_REPORT_TABS = [
  { key: 'summary', label: 'Summary' },
  { key: 'integrity', label: 'Integrity' },
  { key: 'suites', label: 'Suites' },
  { key: 'robustness', label: 'Robustness' },
  { key: 'members', label: 'Members' },
  { key: 'dimensions', label: 'Dimensions' },
  { key: 'speed', label: 'Speed' },
  { key: 'cost', label: 'Cost' },
  { key: 'configuration', label: 'Configuration' },
  { key: 'paired', label: 'Paired Test' },
  { key: 'reports', label: 'AI Reports' }
] as const;

export type BatteryRunReportTabKey = typeof BATTERY_RUN_REPORT_TABS[number]['key'];

export const BATTERY_RUN_REPORT_TAB_STORAGE_KEY = 'overseer.benchmark.batteryRunReport.tab';
export const BATTERY_RUN_REPORT_HEADER_STORAGE_KEY = 'overseer.benchmark.batteryRunReport.header';
export const BATTERY_RUN_REPORT_KEY_FIGURES_STORAGE_KEY = 'overseer.benchmark.batteryRunReport.keyFigures';
export const BATTERY_RUN_REPORT_IMAGE_DETAILS_STORAGE_KEY = 'overseer.benchmark.batteryRunReport.imageDetails';

/** The key figures a battery run shows, in display order; every card is always rendered. */
export const BATTERY_RUN_KEY_FIGURES: readonly KeyFigureKey[] = [
  'intelligence', 'critical-errors', 'answered', 'speed', 'mean-time', 'wall-time', 'model-cost', 'estimated-cost'
];

/** The cost role of the model under test in `totalCostByRole`. */
export const CANDIDATE_COST_ROLE = 'Candidate';

/**
 * A battery's wall clock as the battery report prints it (`BenchmarkBatteryReportBuilder.Duration`):
 * `2 d 3 h 04 min`, `3 h 04 min` or `4 min 05 s`, every part truncated, never rounded up; an em dash
 * without a figure.
 */
export function formatBatteryWallClock(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) {
    return '—';
  }
  const totalSeconds = Math.floor(Math.max(0, ms) / 1000);
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const mm = minutes.toString().padStart(2, '0');
  if (days > 0) {
    return `${days} d ${hours} h ${mm} min`;
  }
  if (hours > 0) {
    return `${hours} h ${mm} min`;
  }
  return `${minutes} min ${seconds.toString().padStart(2, '0')} s`;
}

/** One column of the Configuration tab's suite fingerprints: Run History's label, color class and long name. */
export interface BatterySuiteFingerprintColumn {
  readonly field: 'candidateSystemPromptSha256' | 'toolGuidesSha256' | 'knowledgeBaseHeadSha' | 'wikiHeadSha' | 'sourceCodeHeadSha';
  readonly label: BenchmarkFingerprintEntry['label'];
  readonly cssClass: BenchmarkFingerprintEntry['cssClass'];
  readonly longName: string;
}

/** The five instrument hashes a battery suite records, in Run History's order. */
export const BATTERY_SUITE_FINGERPRINT_COLUMNS: readonly BatterySuiteFingerprintColumn[] = [
  { field: 'candidateSystemPromptSha256', label: 'PROMPT', cssClass: 'fp-prompt', longName: FINGERPRINT_LONG_NAMES.PROMPT },
  { field: 'toolGuidesSha256', label: 'GUIDES', cssClass: 'fp-guides', longName: FINGERPRINT_LONG_NAMES.GUIDES },
  { field: 'knowledgeBaseHeadSha', label: 'KB', cssClass: 'fp-kb', longName: FINGERPRINT_LONG_NAMES.KB },
  { field: 'wikiHeadSha', label: 'WIKI', cssClass: 'fp-wiki', longName: FINGERPRINT_LONG_NAMES.WIKI },
  { field: 'sourceCodeHeadSha', label: 'SRC', cssClass: 'fp-source', longName: FINGERPRINT_LONG_NAMES.SRC }
];

/** A suite's recorded hash for `column`; null when it was not recorded. */
export function batterySuiteFingerprint(suite: BenchmarkBatteryRunSuiteDto, column: BatterySuiteFingerprintColumn): string | null {
  const value = suite[column.field];
  return value ? value : null;
}

const STORAGE_VERSION = 1;

/** The remembered tab; Summary when none is stored, it is unknown, or storage is unavailable. */
export function readStoredBatteryRunReportTab(): BatteryRunReportTabKey {
  try {
    const stored = localStorage.getItem(BATTERY_RUN_REPORT_TAB_STORAGE_KEY);
    return BATTERY_RUN_REPORT_TABS.find(tab => tab.key === stored)?.key ?? 'summary';
  } catch {
    return 'summary';
  }
}

/** Whether the header's Run details is open, from `{ version: 1, detailsOpen }`; closed by default. */
export function readStoredBatteryRunHeaderOpen(): boolean {
  try {
    const raw = localStorage.getItem(BATTERY_RUN_REPORT_HEADER_STORAGE_KEY);
    if (!raw) {
      return false;
    }
    const parsed = JSON.parse(raw) as { version?: unknown; detailsOpen?: unknown } | null;
    return parsed?.version === STORAGE_VERSION && parsed.detailsOpen === true;
  } catch {
    return false;
  }
}

// -----------------------------------------------------------------------------------------------
// Header actions, paired test and diagnostics
// -----------------------------------------------------------------------------------------------

export type BatteryRunReportActionKey = 'recompute' | 'progress';

/** One item of the Actions popover; `reason` set means it is unavailable and says why. */
export interface BatteryRunReportAction {
  readonly key: BatteryRunReportActionKey;
  readonly label: string;
  readonly reason: string | null;
}

/** What this run against the chosen baseline would be, judged from the leaderboard rows alone. */
export type BatteryPairedKind = 'model' | 'replicate' | 'verification' | 'unlikely' | 'none';

/** A leaderboard row offered as a baseline, with the comparability class it belongs to. */
export interface BatteryPairedCandidate {
  readonly classLabel: string;
  readonly classSha: string;
  readonly row: BenchmarkBatteryLeaderboardRowDto;
}

/** One cell of the Members grid. */
export interface BatteryMemberCell {
  readonly round: number;
  readonly state: BatterySlotState;
  readonly member: BenchmarkBatteryMemberDto | null;
}

/** One suite row of the Members grid. */
export interface BatteryMemberRow {
  readonly suiteIndex: number;
  readonly suiteName: string;
  readonly cells: readonly BatteryMemberCell[];
}

/** One card of the Suites tab; `profile` is null until the battery run is analyzed. */
export interface BatterySuiteCard {
  readonly suiteIndex: number;
  /** 1-based position in run order. */
  readonly position: number;
  readonly count: number;
  readonly suiteName: string;
  readonly profile: BenchmarkBatterySuiteProfile | null;
  /** The non-superseded member runs, by round. */
  readonly members: readonly BenchmarkBatteryMemberDto[];
}

function suiteNameOf(
  index: number,
  detail: BenchmarkBatteryRunDto,
  result: BenchmarkBatteryStatisticsResult | null | undefined
): string {
  return result?.suites?.find(s => s.suiteIndex === index)?.suiteName
    ?? detail.suites?.find(s => s.index === index)?.suiteName
    ?? `Suite ${index + 1}`;
}

function memberLine(member: BenchmarkBatteryMemberDto): string {
  const parts = [`run #${member.runId}`, member.runStatus, `index ${formatNumber(member.qualityIndex)}`];
  parts.push(member.usable ? 'usable' : `not usable (${member.unusableReason || 'no reason recorded'})`);
  if (member.origin === 'Attached') {
    parts.push('attached');
  }
  if (member.guardFailure) {
    parts.push(`guard: ${member.guardFailure}`);
  }
  return parts.join(' · ');
}

/**
 * The battery run's diagnostics as plain text: the definition, the status and stop reason, every
 * member by suite and round (superseded ones after the slot's own), the members the analysis left
 * out, and the analysis caveats.
 */
export function batteryRunDiagnosticsText(
  detail: BenchmarkBatteryRunDto,
  analysis: BenchmarkBatteryAnalysisDto | null
): string {
  const result = analysis?.result ?? null;
  const lines: string[] = [];
  lines.push(`Battery Run #${detail.id} diagnostics`);
  lines.push('');
  lines.push('Definition');
  lines.push(`  Battery: ${detail.batteryName} · revision ${detail.definitionRevision} · ${batterySchemeLabel(detail.weightingScheme)}`);
  lines.push(`  Definition SHA-256: ${detail.definitionSha256 || '—'}`);
  lines.push(`  Comparability class: ${analysis?.comparabilityClassSha256 ?? detail.comparabilityClassSha256 ?? '—'}`);
  lines.push(`  Suites (${detail.suiteCount}): ${(detail.suites ?? []).map(s => s.suiteName).join(', ') || '—'}`);
  lines.push(`  Runs per suite: ${detail.runsPerSuite}`);
  lines.push(`  Model: ${detail.testedModelLabel || detail.testedModelId || 'not recorded'}`);
  lines.push(`  Assessor: ${detail.assessorLabel || 'not recorded'}${detail.coAssessorLabel ? ` · co-assessor ${detail.coAssessorLabel}` : ''}`);
  lines.push('');
  lines.push('Status');
  lines.push(`  Status: ${batteryRunStatusLabel(detail.status)}`);
  lines.push(`  Stop reason: ${detail.stopReason ? `${detail.stopReasonText || detail.stopReason} (${detail.stopReason})` : 'none'}`);
  if (detail.errorMessage) {
    lines.push(`  Error: ${detail.errorMessage}`);
  }
  lines.push(`  Resumable: ${detail.resumable ? 'yes' : 'no'} · driven by this server: ${detail.isDriving ? 'yes' : 'no'} · cap wait: ${detail.allowCapWait ? 'allowed' : 'not allowed'}`);
  lines.push(`  Started: ${detail.startedAtUtc || '—'} · completed: ${detail.completedAtUtc || '—'} · last progress: ${detail.lastProgressAtUtc || '—'}`);
  lines.push(`  Members: ${detail.completedMemberCount} of ${detail.requestedMemberCount} usable · ${detail.failedMemberCount} failed · suites complete ${detail.completedSuiteCount} of ${detail.suiteCount}`);
  lines.push('');
  lines.push('Members by suite and round');
  const suites = [...(detail.suites ?? [])].sort((a, b) => a.index - b.index);
  const rounds = Math.max(1, detail.runsPerSuite || 1);
  for (const suite of suites) {
    lines.push(`  ${suite.index + 1}. ${suite.suiteName}`);
    for (let round = 1; round <= rounds; round++) {
      const occupant = (detail.slots ?? []).find(s => s.suiteIndex === suite.index && s.round === round)?.member ?? null;
      lines.push(`    Round ${round}: ${occupant ? memberLine(occupant) : 'empty'}`);
      const superseded = (detail.members ?? []).filter(m => m.superseded && m.suiteIndex === suite.index && m.round === round);
      for (const member of superseded) {
        lines.push(`      superseded: ${memberLine(member)}`);
      }
    }
  }
  lines.push('');
  lines.push('Analysis');
  if (!analysis) {
    lines.push('  No analysis has been computed.');
  } else {
    lines.push(`  Analysis #${analysis.id} computed ${analysis.computedAtUtc} · ${analysis.complete ? 'complete' : 'incomplete'} · ${analysis.stale ? 'stale' : 'current'}`);
    lines.push(`  Harness ${analysis.harnessVersion || '—'} · scoring method ${analysis.scoringMethodVersion} · ${analysis.runCount} member runs`);
    if (result?.overallIndex) {
      lines.push(`  Overall Index: ${formatIndexWithHalfWidth(result.overallIndex.pointEstimate, result.overallIndex.combinedHalfWidth)} ${formatInterval(result.overallIndex.combinedLower, result.overallIndex.combinedUpper)}`);
    } else if (result) {
      lines.push(`  Overall Index: not reported (${result.completedSuiteCount} of ${result.suiteCount} suites complete)`);
    }
  }
  lines.push('');
  lines.push('Excluded members');
  const excluded = analysis?.excludedMembers ?? [];
  if (excluded.length === 0) {
    lines.push('  None');
  } else {
    for (const member of excluded) {
      lines.push(`  ${suiteNameOf(member.suiteIndex, detail, result)}, round ${member.round}, run #${member.runId}: ${member.reason}`);
    }
  }
  lines.push('');
  lines.push('Caveats');
  const caveats = result?.caveats ?? [];
  if (caveats.length === 0) {
    lines.push('  None');
  } else {
    for (const caveat of caveats) {
      lines.push(`  - ${caveat}`);
    }
  }
  return lines.join('\n') + '\n';
}

/**
 * The Battery Run Report: a full-screen dialog in the single-run report's frame, with the battery's
 * header facts, its actions, and eleven tabs over its analysis, members, configuration, paired test
 * and AI-written documents.
 *
 * It loads the battery run and its latest analysis itself, and polls the battery run while it is
 * live; closing stops the polling. A member run's single-run report is the host's: the dialog emits
 * `openRunReport` with the run id.
 */
@Component({
  selector: 'app-battery-run-report-dialog',
  standalone: true,
  imports: [
    RunReportFrameComponent, RunFactsComponent, KeyFigureCardActionsComponent, KeyFiguresChooserComponent,
    BenchmarkDownloadCenterComponent, BatteryAiReportsComponent, PairedTestResultComponent, InfoTipComponent
  ],
  templateUrl: './battery-run-report-dialog.component.html',
  styleUrls: ['./battery-run-report-dialog.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BatteryRunReportDialogComponent implements OnInit, OnDestroy {
  private readonly benchmarkService = inject(AdminBenchmarkService);
  private readonly systemService = inject(SystemService);
  private readonly monitor = inject(BenchmarkActiveRunMonitor, { optional: true });
  private readonly cdr = inject(ChangeDetectorRef);

  static readonly POLL_INTERVAL_MS = 5000;
  /** Poll delays after consecutive failures; the last repeats. */
  static readonly POLL_BACKOFF_MS = [10000, 20000, 30000] as const;

  /** Emitted once each time the dialog closes, however it was closed. */
  @Output() readonly closed = new EventEmitter<void>();
  /** A member run's id; the host opens that run's report over this dialog. */
  @Output() readonly openRunReport = new EventEmitter<number>();
  /**
   * A member run's diagnostics text, as that run's own Download Center captures it, for the Download
   * Center to list each member run's diagnostics; without it they are not listed.
   */
  @Input() memberDiagnosticsText?: (run: BenchmarkRunDetailDto) => string;

  @ViewChild('brrDialog', { static: true }) dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild('brrTitle') title?: ElementRef<HTMLElement>;
  @ViewChild(RunReportFrameComponent) frame?: RunReportFrameComponent;
  @ViewChild('brrDownloadCenter') downloadCenter?: BenchmarkDownloadCenterComponent;
  @ViewChild(KeyFiguresChooserComponent) keyFiguresChooser?: KeyFiguresChooserComponent;
  @ViewChild('actionsTrigger') actionsTrigger?: ElementRef<HTMLButtonElement>;
  @ViewChild('actionsPopover') actionsPopover?: ElementRef<HTMLElement>;

  readonly tabs = BATTERY_RUN_REPORT_TABS;
  readonly slotLabels = BATTERY_SLOT_STATE_LABELS;
  readonly formatCost = formatCost;
  readonly formatIndexWithHalfWidth = formatIndexWithHalfWidth;
  readonly formatInterval = formatInterval;
  readonly formatMs = formatMs;
  readonly formatWallClock = formatBatteryWallClock;
  readonly formatNumber = formatNumber;
  readonly formatPercent = formatPercent;
  readonly formatPValue = formatPValue;
  readonly formatSigned = formatSigned;
  readonly schemeLabel = batterySchemeLabel;
  readonly statusLabel = batteryRunStatusLabel;
  readonly statusBadgeClass = batteryStatusBadgeClass;
  readonly scoreClass = getScoreBadgeClass;
  readonly reproducibilitySourceLabel = reproducibilitySourceLabel;
  readonly randomizationMethodLabel = randomizationMethodLabel;
  readonly fingerprintColumns = BATTERY_SUITE_FINGERPRINT_COLUMNS;
  readonly suiteFingerprint = batterySuiteFingerprint;

  /** The battery run the dialog shows, from `open()` until it closes. */
  batteryRunId: number | null = null;
  detail: BenchmarkBatteryRunDto | null = null;
  loading = false;
  loadError: string | null = null;

  analysis: BenchmarkBatteryAnalysisDto | null = null;
  analysisLoading = false;
  analysisError: string | null = null;
  recomputing = false;

  tab: BatteryRunReportTabKey = 'summary';
  headerDetailsOpen = readStoredBatteryRunHeaderOpen();
  actionsOpen = false;
  /** The polite status line under the header actions. */
  copyStatus = '';

  keyFiguresExporting = false;
  keyFigureExclusions: string[] = readStoredKeyFigureExclusions(BATTERY_RUN_REPORT_KEY_FIGURES_STORAGE_KEY);
  imageDetailExclusions: string[] = readStoredImageDetailExclusions(BATTERY_RUN_REPORT_IMAGE_DETAILS_STORAGE_KEY, []);

  // Paired test
  leaderboardHash: string | null = null;
  leaderboard: BenchmarkBatteryLeaderboardDto | null = null;
  leaderboardLoading = false;
  leaderboardError: string | null = null;
  compareBaselineId: number | null = null;
  comparing = false;
  comparison: BenchmarkBatteryAnalysisDto | null = null;
  compareRefusal: string | null = null;
  /** The same pair on every measure: M7 again for Intelligence, pooled for the dimensions, speed and cost. */
  pairedResult: BenchmarkPairComparisonDto | null = null;
  pairedLoading = false;
  pairedError: string | null = null;
  /** The paired result's Intelligence verdict, shown with the M7 headline. */
  pairedIntelligenceVerdict: { text: string; shape: VerdictShape } | null = null;

  private isOpen = false;
  private loadToken = 0;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollInFlight = false;
  private failureCount = 0;
  private statusTimer: ReturnType<typeof setTimeout> | null = null;
  private overseerVersion: string | null = null;
  /** When the battery run was last read; a live duration runs to it, so it is stable between checks. */
  private detailReadAtUtc: string | null = null;

  private factsSource: BenchmarkBatteryRunDto | null = null;
  private factsSplit: { rows: RunFactRow[]; primary: RunFactRow[]; detail: RunFactRow[]; readout: string } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.clearStatusTimer();
  }

  // =============================================================================================
  // Open and close
  // =============================================================================================

  /** Shows the battery run's report on the remembered tab, focuses its title and loads it. */
  open(batteryRunId: number): void {
    this.loadToken++;
    this.stopPolling();
    if (!this.isOpen) {
      this.tab = readStoredBatteryRunReportTab();
    }
    if (this.batteryRunId !== batteryRunId || !this.isOpen) {
      this.resetState();
    }
    this.batteryRunId = batteryRunId;
    this.isOpen = true;
    const dialog = this.dialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
    this.cdr.detectChanges();
    this.title?.nativeElement.focus();
    this.load();
  }

  /** Closes the dialog; the cleanup runs once whichever way it closed. */
  close(): void {
    const dialog = this.dialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
    this.onDialogClosed();
  }

  /** Escape: the dialog's own cancel closes it through `close()`. */
  onCancel(event: Event): void {
    if (event.target !== this.dialog?.nativeElement) {
      return;
    }
    event.preventDefault();
    this.close();
  }

  /** The dialog's close event, and `close()`: stops polling, clears the run and emits `closed`. */
  onDialogClosed(event?: Event): void {
    const dialog = this.dialog?.nativeElement;
    if ((event && event.target !== dialog) || dialog?.open || !this.isOpen) {
      return;
    }
    this.isOpen = false;
    this.loadToken++;
    this.stopPolling();
    this.clearStatusTimer();
    this.resetState();
    this.batteryRunId = null;
    this.cdr.markForCheck();
    this.closed.emit();
  }

  private resetState(): void {
    this.detail = null;
    this.loading = false;
    this.loadError = null;
    this.analysis = null;
    this.analysisLoading = false;
    this.analysisError = null;
    this.recomputing = false;
    this.actionsOpen = false;
    this.copyStatus = '';
    this.keyFiguresExporting = false;
    this.leaderboardHash = null;
    this.leaderboard = null;
    this.leaderboardLoading = false;
    this.leaderboardError = null;
    this.compareBaselineId = null;
    this.comparing = false;
    this.comparison = null;
    this.compareRefusal = null;
    this.pairedResult = null;
    this.pairedIntelligenceVerdict = null;
    this.pairedLoading = false;
    this.pairedError = null;
    this.failureCount = 0;
  }

  // =============================================================================================
  // Loading and polling
  // =============================================================================================

  private load(): void {
    const id = this.batteryRunId;
    if (id == null) {
      return;
    }
    const token = this.loadToken;
    this.loading = true;
    this.loadError = null;
    this.pollInFlight = true;
    this.benchmarkService.getBatteryRun(id).subscribe({
      next: (detail) => {
        if (token !== this.loadToken) return;
        this.pollInFlight = false;
        this.loading = false;
        this.failureCount = 0;
        this.applyDetail(detail);
        this.scheduleNextPoll();
        this.cdr.markForCheck();
        // The header's tooltips and the Actions popover are new anchors to the polyfill.
        refreshAnchorPositioning();
      },
      error: (err) => {
        if (token !== this.loadToken) return;
        this.pollInFlight = false;
        this.loading = false;
        this.loadError = this.loadErrorOf(err, id);
        this.cdr.markForCheck();
      }
    });
    this.loadAnalysis();
  }

  /** Try again after a failed load. */
  retryLoad(): void {
    if (this.batteryRunId != null && this.isOpen) {
      this.loadToken++;
      this.stopPolling();
      this.load();
    }
  }

  private loadErrorOf(err: unknown, id: number): string {
    const status = (err as { status?: number } | null)?.status;
    if (status === 404) {
      return `Battery run #${id} no longer exists.`;
    }
    if (status === 0) {
      return `Battery run #${id} could not be loaded: the server could not be reached.`;
    }
    return httpErrorText(err, `Battery run #${id} could not be loaded.`);
  }

  private loadAnalysis(): void {
    const id = this.batteryRunId;
    if (id == null) {
      return;
    }
    const token = this.loadToken;
    this.analysisLoading = true;
    this.analysisError = null;
    this.benchmarkService.getBatteryAnalysis(id).subscribe({
      next: (analysis) => {
        if (token !== this.loadToken) return;
        this.analysis = analysis;
        this.analysisLoading = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.loadToken) return;
        this.analysisLoading = false;
        this.analysisError = httpErrorText(err, 'Could not load the analysis.');
        this.cdr.markForCheck();
      }
    });
  }

  /** Takes a fresh battery run; a new or newly stale analysis is fetched again. */
  private applyDetail(detail: BenchmarkBatteryRunDto): void {
    const previous = this.detail;
    this.detail = detail;
    this.detailReadAtUtc = new Date().toISOString();
    if (previous && (previous.latestAnalysisId !== detail.latestAnalysisId || previous.analysisStale !== detail.analysisStale)) {
      this.loadAnalysis();
    }
    if (this.tab === 'paired') {
      this.ensureLeaderboard();
    }
  }

  get isLive(): boolean {
    return isLiveBatteryRunStatus(this.detail?.status);
  }

  /** Polls again while the battery run is live and the dialog open. */
  private scheduleNextPoll(): void {
    if (this.isOpen && this.isLive) {
      this.schedulePoll(BatteryRunReportDialogComponent.POLL_INTERVAL_MS);
    }
  }

  private schedulePoll(delayMs: number): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
    }
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      this.poll();
    }, delayMs);
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
    this.pollInFlight = false;
  }

  /** One request at a time; a hidden page is not read, and failures back off. */
  private poll(): void {
    const id = this.batteryRunId;
    if (!this.isOpen || id == null || this.pollInFlight) {
      return;
    }
    if (typeof document !== 'undefined' && document.hidden) {
      this.schedulePoll(BatteryRunReportDialogComponent.POLL_INTERVAL_MS);
      return;
    }
    const token = this.loadToken;
    this.pollInFlight = true;
    this.benchmarkService.getBatteryRun(id).subscribe({
      next: (detail) => {
        if (token !== this.loadToken) return;
        this.pollInFlight = false;
        this.failureCount = 0;
        this.applyDetail(detail);
        this.scheduleNextPoll();
        this.cdr.markForCheck();
      },
      error: () => {
        if (token !== this.loadToken) return;
        this.pollInFlight = false;
        const backoff = BatteryRunReportDialogComponent.POLL_BACKOFF_MS;
        const delay = backoff[Math.min(this.failureCount, backoff.length - 1)];
        this.failureCount++;
        if (this.isOpen) {
          this.schedulePoll(delay);
        }
        this.cdr.markForCheck();
      }
    });
  }

  /** Reads the battery run once now, and goes on polling while it is live. */
  private refreshDetail(): void {
    this.stopPolling();
    this.poll();
  }

  // =============================================================================================
  // Header
  // =============================================================================================

  /** The header facts, rebuilt only when the battery run object is replaced. */
  get facts(): RunFactRow[] {
    const detail = this.detail;
    if (!detail) {
      return [];
    }
    return this.splitFacts(detail).rows;
  }

  /** Model and Assessor(s), always shown. */
  get primaryFacts(): RunFactRow[] {
    return this.detail ? this.splitFacts(this.detail).primary : [];
  }

  /** The rest, inside Run details. */
  get detailFacts(): RunFactRow[] {
    return this.detail ? this.splitFacts(this.detail).detail : [];
  }

  /** Run details' one-line read-out, shown in its summary while it is closed. */
  get factsReadout(): string {
    return this.detail ? this.splitFacts(this.detail).readout : '';
  }

  private splitFacts(detail: BenchmarkBatteryRunDto): { rows: RunFactRow[]; primary: RunFactRow[]; detail: RunFactRow[]; readout: string } {
    if (!this.factsSplit || this.factsSource !== detail) {
      const rows = buildBatteryRunFacts(detail);
      const primaryKeys: readonly string[] = RUN_FACT_PRIMARY_KEYS;
      const rest = rows.filter(row => !primaryKeys.includes(row.key));
      this.factsSource = detail;
      this.factsSplit = {
        rows,
        primary: rows.filter(row => primaryKeys.includes(row.key)),
        detail: rest,
        readout: runFactsReadout(rest)
      };
    }
    return this.factsSplit;
  }

  /** Follows Run details' native toggle and remembers it. */
  onHeaderDetailsToggle(event: Event): void {
    const details = event.target as HTMLDetailsElement | null;
    if (!details || details !== event.currentTarget) {
      return;
    }
    this.headerDetailsOpen = details.open;
    try {
      localStorage.setItem(BATTERY_RUN_REPORT_HEADER_STORAGE_KEY,
        JSON.stringify({ version: STORAGE_VERSION, detailsOpen: details.open }));
    } catch {
      // Storage unavailable: the choice lasts until the page reloads.
    }
  }

  // --- Downloads ---

  /** The button that last opened the Download Center, when it is not the header's Downloads. */
  private downloadsOpener: HTMLElement | null = null;

  /**
   * Opens the Download Center on this battery run's analysis report and documents. `opener` is the
   * button that asked, when it is not the header's Downloads (the AI Reports tab's).
   */
  openDownloads(opener?: HTMLElement): void {
    const detail = this.detail;
    if (!detail) {
      return;
    }
    this.downloadsOpener = opener ?? null;
    this.downloadCenter?.open({
      kind: 'battery',
      batteryRunId: detail.id,
      label: `${detail.batteryName} · ${detail.testedModelLabel || 'unknown model'}`,
      ...(this.memberDiagnosticsText ? { memberDiagnosticsText: this.memberDiagnosticsText } : {})
    });
  }

  /** Focus lost when the Download Center closed goes back to the button that opened it, else to Downloads. */
  onDownloadsClosed(): void {
    const opener = this.downloadsOpener;
    this.downloadsOpener = null;
    const active = document.activeElement;
    if (this.isOpen && (!active || active === document.body)) {
      const target = opener?.isConnected && opener.getClientRects().length > 0
        ? opener
        : document.getElementById('brr-downloads-trigger');
      target?.focus();
    }
  }

  /** Whether the dialog is open; the AI Reports tab polls only then. */
  get dialogOpen(): boolean {
    return this.isOpen;
  }

  /** The AI Reports tab read new report fields: the battery run the dialog shows follows them. */
  onReportStatusChange(change: BatteryReportStatusChange): void {
    const detail = this.detail;
    if (!detail) {
      return;
    }
    detail.reportDocumentsStatus = change.status;
    detail.reportDocumentsMessage = change.message;
    detail.reportWriterModelConfigurationId = change.writerId;
    this.cdr.markForCheck();
  }

  // --- Actions popover ---

  /**
   * The Actions popover's items; an unavailable one carries its reason. Continue and Re-run under
   * current instrument live in the battery progress dialog, which *Show progress* opens.
   */
  get reportActions(): BatteryRunReportAction[] {
    const detail = this.detail;
    if (!detail) {
      return [];
    }

    const recompute: BatteryRunReportAction = {
      key: 'recompute',
      label: this.recomputing ? 'Computing…' : (this.analysis ? 'Recompute analysis' : 'Compute analysis'),
      reason: this.recomputing ? 'The analysis is being computed.' : null
    };

    let progressReason: string | null = null;
    if (!this.monitor) {
      progressReason = 'Battery progress is not available here.';
    } else if (!this.isLive && !detail.resumable && detail.status !== 'Stopped' && !batteryAwaitsPostRun(detail)) {
      progressReason = 'The battery run has finished.';
    }

    return [
      recompute,
      { key: 'progress', label: 'Show progress', reason: progressReason }
    ];
  }

  onAction(action: BatteryRunReportAction): void {
    if (action.reason) {
      return;
    }
    this.closeActionsPopover();
    switch (action.key) {
      case 'recompute':
        this.recompute();
        break;
      case 'progress':
        this.showProgress();
        break;
    }
  }

  /** The popover's toggle event: aria-expanded, and focus into the popover or back to the trigger. */
  onActionsToggle(event: Event): void {
    const open = (event as ToggleEvent).newState === 'open';
    this.actionsOpen = open;
    const popover = this.actionsPopover?.nativeElement;
    if (open) {
      refreshAnchorPositioning();
      popover?.querySelector<HTMLElement>('.gh-action-popover-item:not([aria-disabled="true"])')?.focus();
    } else {
      const active = document.activeElement;
      if (!active || active === document.body || !!popover?.contains(active)) {
        this.actionsTrigger?.nativeElement.focus();
      }
    }
    this.cdr.markForCheck();
  }

  /** Escape closes the popover only; the dialog stays open. */
  onActionsPopoverKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Escape') {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.closeActionsPopover();
  }

  private closeActionsPopover(): void {
    try {
      this.actionsPopover?.nativeElement.hidePopover();
    } catch {
      // Already hidden.
    }
    this.actionsOpen = false;
    this.actionsTrigger?.nativeElement.focus();
    this.cdr.markForCheck();
  }

  /** Computes and stores the analysis again, then rereads the battery run's analysis flags. */
  recompute(): void {
    const id = this.detail?.id;
    if (id == null || this.recomputing) {
      return;
    }
    const token = this.loadToken;
    this.recomputing = true;
    this.analysisError = null;
    this.cdr.markForCheck();
    this.benchmarkService.analyseBatteryRun(id).subscribe({
      next: (analysis) => {
        if (token !== this.loadToken) return;
        this.recomputing = false;
        this.analysis = analysis;
        this.refreshDetail();
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.loadToken) return;
        this.recomputing = false;
        this.analysisError = httpErrorText(err, 'The analysis could not be computed.');
        this.cdr.markForCheck();
      }
    });
  }

  /** Closes the report and opens the battery progress dialog on this battery run. */
  private showProgress(): void {
    const id = this.detail?.id;
    if (id == null || !this.monitor) {
      return;
    }
    this.close();
    this.monitor.openBatteryDialog(id);
  }

  // --- Copy diagnostics ---

  async copyDiagnostics(): Promise<void> {
    const detail = this.detail;
    if (!detail) {
      return;
    }
    const copied = await copyToClipboard(batteryRunDiagnosticsText(detail, this.analysis));
    this.announce(copied ? 'Diagnostics copied to the clipboard.' : 'Could not copy the diagnostics to the clipboard.');
  }

  private announce(message: string): void {
    this.clearStatusTimer();
    this.copyStatus = message;
    this.statusTimer = setTimeout(() => {
      this.copyStatus = '';
      this.statusTimer = null;
      this.cdr.markForCheck();
    }, COPY_STATUS_MS);
    this.cdr.markForCheck();
  }

  private clearStatusTimer(): void {
    if (this.statusTimer !== null) {
      clearTimeout(this.statusTimer);
      this.statusTimer = null;
    }
  }

  // =============================================================================================
  // Tabs
  // =============================================================================================

  selectTab(key: BatteryRunReportTabKey): void {
    this.tab = key;
    try {
      localStorage.setItem(BATTERY_RUN_REPORT_TAB_STORAGE_KEY, key);
    } catch {
      // Storage unavailable: the choice lasts until the page reloads.
    }
    if (key === 'paired') {
      this.ensureLeaderboard();
    }
    this.frame?.scrollBodyToTop();
    this.cdr.markForCheck();
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const count = BATTERY_RUN_REPORT_TABS.length;
    const targets: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: count - 1 };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }
    event.preventDefault();
    const key = BATTERY_RUN_REPORT_TABS[(requested + count) % count].key;
    this.selectTab(key);
    this.cdr.detectChanges();
    document.getElementById(`brr-tab-${key}`)?.focus();
  }

  // =============================================================================================
  // Analysis figures
  // =============================================================================================

  get result(): BenchmarkBatteryStatisticsResult | null {
    return this.analysis?.result ?? null;
  }

  /** The Overall Index, only when the analysis is complete. */
  get headline(): BenchmarkBatteryOverallIndex | null {
    const result = this.result;
    return result?.complete && result.overallIndex ? result.overallIndex : null;
  }

  /** The analysis may no longer describe the battery run: stale, or it left out members. */
  get recomputeCalledOut(): boolean {
    const detail = this.detail;
    return !!this.analysis && (
      this.analysis.stale
      || (this.analysis.excludedMembers?.length ?? 0) > 0
      || !!detail?.analysisStale
      || !!detail?.analysisHasExcludedMembers);
  }

  get analysisStale(): boolean {
    return !!this.analysis?.stale || !!this.detail?.analysisStale;
  }

  get excludedCount(): number {
    return this.analysis?.excludedMembers?.length ?? 0;
  }

  /**
   * The Integrity notice's clauses: a stale analysis, members left out, an incomplete battery, an
   * approximate pooled identity and a speed or cost comparison degraded across suites.
   */
  get integrityNotices(): string[] {
    const detail = this.detail;
    if (!detail) {
      return [];
    }
    const result = this.result;
    const notices: string[] = [];
    if (this.analysisStale) {
      notices.push('The battery run\'s usable members changed after this analysis was computed.');
    }
    const excluded = this.excludedCount;
    if (excluded > 0) {
      notices.push(`${excluded} member ${excluded === 1 ? 'run is' : 'runs are'} left out of the analysis.`);
    } else if (detail.analysisHasExcludedMembers) {
      notices.push('The latest analysis leaves out members that were not usable.');
    }
    if (result && !result.complete) {
      notices.push(`Incomplete: ${result.completedSuiteCount} of ${result.suiteCount} suites have a usable result, so there is no Overall Index.`);
    }
    if (result?.complete && !result.pooledIdentityHolds) {
      notices.push('The pooled identity is approximate: some exam question has no scored answer, or an exam is shorter than its members\' question count.');
    }
    if (result?.speed?.degraded) {
      notices.push(`Speed is degraded: ${result.speed.degradedReason || 'a speed-affecting setting differs between suites.'}`);
    }
    if (result?.cost?.degraded) {
      notices.push(`Cost is degraded: ${result.cost.degradedReason || 'a cost-affecting setting differs between suites.'}`);
    }
    return notices;
  }

  /** The one condition the Integrity tab's Notice tag and the notice itself both read. */
  get hasIntegrityNotice(): boolean {
    return this.integrityNotices.length > 0;
  }

  suiteName(index: number): string {
    return this.detail ? suiteNameOf(index, this.detail, this.result) : `Suite ${index + 1}`;
  }

  suiteNames(indexes: readonly number[] | null | undefined): string {
    return (indexes ?? []).map(i => this.suiteName(i)).join(', ');
  }

  /** The declared scheme's index, for the sensitivity differences. */
  get declaredSchemeIndex(): number | null {
    return this.result?.weightingSensitivity?.find(s => s.declared)?.index ?? null;
  }

  roleCosts(costs: Record<string, number> | null | undefined): { role: string; cost: number }[] {
    return Object.entries(costs ?? {}).map(([role, cost]) => ({ role, cost }));
  }

  toolFamilies(families: Record<string, number> | null | undefined): { family: string; count: number }[] {
    return Object.entries(families ?? {})
      .map(([family, count]) => ({ family, count }))
      .sort((a, b) => b.count - a.count || a.family.localeCompare(b.family));
  }

  /** Scored items over exam items, across every suite of the result. */
  get answeredFigure(): { scored: number; exam: number } | null {
    const suites = this.result?.suites ?? [];
    if (suites.length === 0) {
      return null;
    }
    return {
      scored: suites.reduce((sum, s) => sum + (s.scoredItemCount ?? 0), 0),
      exam: suites.reduce((sum, s) => sum + (s.examItemCount ?? 0), 0)
    };
  }

  get speedIndex(): number | null {
    return this.result?.speed?.overallSpeedIndex ?? this.detail?.overallSpeedIndex ?? null;
  }

  /** The battery's elapsed time: start to completion, or, while live, to when the battery run was last read. */
  get durationMs(): number | null {
    const detail = this.detail;
    if (!detail?.startedAtUtc) {
      return null;
    }
    if (detail.completedAtUtc) {
      return elapsedMsBetween(detail.startedAtUtc, detail.completedAtUtc);
    }
    return this.isLive ? elapsedMsBetween(detail.startedAtUtc, this.detailReadAtUtc) : null;
  }

  get candidateCost(): number | null {
    return this.result?.cost?.totalCostByRole?.[CANDIDATE_COST_ROLE] ?? null;
  }

  get totalCost(): number | null {
    return this.result?.cost?.totalCost ?? this.detail?.totalCost ?? null;
  }

  /** `28 % of the total cost`, or null without both figures. */
  get candidateCostShare(): string | null {
    const candidate = this.candidateCost;
    const total = this.totalCost;
    if (candidate == null || total == null || !(total > 0)) {
      return null;
    }
    return `${Math.round((candidate / total) * 100)} % of the total cost`;
  }

  // =============================================================================================
  // Key figures
  // =============================================================================================

  isKeyFigureShown(key: KeyFigureKey): boolean {
    return !this.keyFigureExclusions.includes(key);
  }

  get selectedKeyFigureCount(): number {
    return BATTERY_RUN_KEY_FIGURES.filter(key => !this.keyFigureExclusions.includes(key)).length;
  }

  /** `5 of 8` while the selection leaves a figure out, else null. */
  get keyFiguresSelectionLabel(): string | null {
    const total = BATTERY_RUN_KEY_FIGURES.length;
    const selected = this.selectedKeyFigureCount;
    return selected < total ? `${selected} of ${total}` : null;
  }

  openKeyFiguresChooser(opener?: HTMLElement): void {
    const root = this.dialog?.nativeElement.querySelector('.rr-figures');
    if (!this.detail || !root || !this.keyFiguresChooser) {
      return;
    }
    const figures = readKeyFigureCells(root).map(cell => ({ key: cell.key, label: cell.label, value: cell.value }));
    const details = {
      rows: this.facts.map(row => ({ key: row.key, label: row.label, value: runFactPlainText(row) })),
      excluded: this.imageDetailExclusions
    };
    this.keyFiguresChooser.open(figures, this.keyFigureExclusions, opener ?? null, details, readStoredKeyFiguresExportSettings());
  }

  /**
   * The key-figures download settings, shared with the run report and read from storage at every
   * use; the same object while storage is unchanged.
   */
  get keyFiguresExportSettings(): KeyFiguresExportSettings {
    return readStoredKeyFiguresExportSettings();
  }

  /** `PNG` or `WebP`: the format the Download buttons write. */
  get keyFiguresDownloadFormat(): string {
    return keyFiguresFormatLabel(this.keyFiguresExportSettings.format);
  }

  /** The chooser's footer summary: the next whole-strip image of this battery run, measured without drawing. */
  readonly keyFiguresMeasurer: KeyFiguresImageMeasurer = settings => {
    const detail = this.detail;
    const root = this.dialog?.nativeElement.querySelector('.rr-figures');
    if (!detail || !root) {
      return null;
    }
    const excluded = new Set(this.keyFigureExclusions);
    return measureKeyFiguresImage(root, this.keyFiguresContext(detail), key => !excluded.has(key), settings);
  };

  /** The chooser's download settings: remembered at once, for both report dialogs. */
  onKeyFiguresExportSettingsChange(settings: KeyFiguresExportSettings): void {
    writeStoredKeyFiguresExportSettings(settings);
    this.cdr.markForCheck();
  }

  onKeyFigureSelectionChange(excluded: string[]): void {
    this.keyFigureExclusions = [...excluded];
    storeKeyFigureExclusions(this.keyFigureExclusions, BATTERY_RUN_REPORT_KEY_FIGURES_STORAGE_KEY);
    this.cdr.markForCheck();
  }

  onImageDetailSelectionChange(excluded: string[]): void {
    this.imageDetailExclusions = [...excluded];
    storeImageDetailExclusions(this.imageDetailExclusions, BATTERY_RUN_REPORT_IMAGE_DETAILS_STORAGE_KEY);
    this.cdr.markForCheck();
  }

  /** What the images say about the battery run: its header facts, limited to the chosen image details. */
  keyFiguresContext(detail: BenchmarkBatteryRunDto): ImageContext {
    const rows = detail === this.detail ? this.facts : buildBatteryRunFacts(detail);
    return {
      title: `Battery Run #${detail.id} · ${detail.batteryName}`,
      facts: toImageFactRows(
        rows.filter(row => !this.imageDetailExclusions.includes(row.key)),
        { text: batteryRunStatusLabel(detail.status), tone: statusImageTone(batteryStatusBadgeClass(detail.status)) }
      ),
      runId: detail.id,
      overseerVersion: this.overseerVersion,
      suiteName: detail.batteryName,
      modelName: detail.testedModelLabel || detail.testedModelId || 'unknown model',
      fileStem: `battery-run-${detail.id}`
    };
  }

  copyKeyFigures(): Promise<void> {
    return this.exportKeyFigures('copy', null);
  }

  downloadKeyFigures(): Promise<void> {
    return this.exportKeyFigures('download', null);
  }

  exportKeyFigureCard(request: KeyFigureCardExportRequest): Promise<void> {
    return this.exportKeyFigures(request.action, request.card);
  }

  /**
   * Composes the selected key figures (`card` null) or one card, copies or saves it, and announces it.
   * The download settings are read from storage now, so a change made in the run report applies.
   */
  private async exportKeyFigures(action: KeyFiguresAction, card: HTMLElement | null): Promise<void> {
    const detail = this.detail;
    const root = this.dialog?.nativeElement.querySelector('.rr-figures');
    if (!detail || !root || this.keyFiguresExporting) {
      return;
    }
    this.keyFiguresExporting = true;
    this.cdr.markForCheck();
    try {
      if (this.overseerVersion === null) {
        this.overseerVersion = await firstValueFrom(this.systemService.getVersion()).catch(() => 'unknown');
      }
      const excluded = new Set(this.keyFigureExclusions);
      const message = await exportKeyFiguresImage(action, root, card, this.keyFiguresContext(detail), key => !excluded.has(key),
        readStoredKeyFiguresExportSettings());
      this.announce(message);
    } finally {
      this.keyFiguresExporting = false;
      this.cdr.markForCheck();
    }
  }

  // =============================================================================================
  // Suites and Members
  // =============================================================================================

  /** The non-superseded members of one suite, by round. */
  suiteMembers(suiteIndex: number): BenchmarkBatteryMemberDto[] {
    return (this.detail?.slots ?? [])
      .filter(slot => slot.suiteIndex === suiteIndex && !!slot.member)
      .map(slot => slot.member as BenchmarkBatteryMemberDto)
      .sort((a, b) => a.round - b.round);
  }

  /**
   * The Suites tab's cards: the analysis's suites with their profiles, else the battery run's suites
   * in run order. Rebuilt only when the battery run or the analysis result is replaced.
   */
  get suiteCards(): BatterySuiteCard[] {
    const detail = this.detail;
    if (!detail) {
      return [];
    }
    const result = this.result;
    if (this.suiteCardsSource !== detail || this.suiteCardsResult !== result) {
      this.suiteCardsSource = detail;
      this.suiteCardsResult = result;
      const indexes = result
        ? result.suites.map(profile => profile.suiteIndex)
        : [...(detail.suites ?? [])].sort((a, b) => a.index - b.index).map(suite => suite.index);
      const count = result?.suiteCount ?? (detail.suiteCount || indexes.length);
      this.suiteCardRows = indexes.map(index => ({
        suiteIndex: index,
        position: index + 1,
        count,
        suiteName: suiteNameOf(index, detail, result),
        profile: result?.suites.find(profile => profile.suiteIndex === index) ?? null,
        members: this.suiteMembers(index)
      }));
    }
    return this.suiteCardRows;
  }

  private suiteCardsSource: BenchmarkBatteryRunDto | null = null;
  private suiteCardsResult: BenchmarkBatteryStatisticsResult | null = null;
  private suiteCardRows: BatterySuiteCard[] = [];

  get rounds(): number[] {
    const count = Math.max(1, this.detail?.runsPerSuite || 1);
    return Array.from({ length: count }, (_, i) => i + 1);
  }

  /** The suite × round grid, rebuilt only when the battery run object is replaced. */
  get memberGrid(): BatteryMemberRow[] {
    const detail = this.detail;
    if (!detail) {
      return [];
    }
    if (this.gridSource !== detail) {
      const members = detail.members ?? [];
      this.gridSource = detail;
      this.gridRows = [...(detail.suites ?? [])]
        .sort((a, b) => a.index - b.index)
        .map(suite => ({
          suiteIndex: suite.index,
          suiteName: suite.suiteName,
          cells: this.rounds.map(round => {
            const slot = (detail.slots ?? []).find(s => s.suiteIndex === suite.index && s.round === round)
              ?? { suiteIndex: suite.index, round, member: null };
            return { round, state: batterySlotState(slot, members), member: slot.member ?? null };
          })
        }));
    }
    return this.gridRows;
  }

  private gridSource: BenchmarkBatteryRunDto | null = null;
  private gridRows: BatteryMemberRow[] = [];

  chipClass(state: BatterySlotState): string {
    switch (state) {
      case 'pending': return 'job-status-chip status-pending';
      case 'running': return 'job-status-chip status-answering';
      case 'completed': return 'job-status-chip status-completed';
      case 'indexWithheld': return 'job-status-chip status-partial';
      case 'instrumentChanged': return 'job-status-chip brr-chip-instrument';
      case 'failed': return 'job-status-chip status-failed';
      case 'superseded': return 'job-status-chip status-canceled';
    }
  }

  /** A member run's single-run report, over this dialog. */
  openMemberRun(runId: number): void {
    this.openRunReport.emit(runId);
  }

  // =============================================================================================
  // Configuration
  // =============================================================================================

  /** The declared weight of suite `index`, from the analysis. */
  declaredWeight(index: number): number | null {
    return this.result?.weights?.[index] ?? null;
  }

  formatDate(value: string | null | undefined): string {
    if (!value) return '—';
    const date = parseServerUtcDate(value);
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
  }

  isoDate(value: string | null | undefined): string | null {
    if (!value) return null;
    const date = parseServerUtcDate(value);
    return Number.isNaN(date.getTime()) ? null : date.toISOString();
  }

  // =============================================================================================
  // Paired Test
  // =============================================================================================

  /** Loads the definition's leaderboard once per definition, for the baseline choices. */
  private ensureLeaderboard(): void {
    const hash = this.detail?.definitionSha256;
    if (!hash || (this.leaderboardHash === hash && (this.leaderboard || this.leaderboardLoading))) {
      return;
    }
    const token = this.loadToken;
    this.leaderboardHash = hash;
    this.leaderboard = null;
    this.leaderboardLoading = true;
    this.leaderboardError = null;
    this.benchmarkService.getBatteryLeaderboard(hash).subscribe({
      next: (board) => {
        if (token !== this.loadToken || this.leaderboardHash !== hash) return;
        this.leaderboard = board;
        this.leaderboardLoading = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.loadToken || this.leaderboardHash !== hash) return;
        this.leaderboardLoading = false;
        this.leaderboardError = httpErrorText(err, 'Could not load the battery runs of this definition.');
        this.cdr.markForCheck();
      }
    });
  }

  /** The definition's ranked results other than this battery run, grouped by comparability class. */
  get pairedClasses(): { label: string; sha: string; rows: BenchmarkBatteryLeaderboardRowDto[] }[] {
    const id = this.detail?.id;
    return (this.leaderboard?.classes ?? [])
      .map(c => ({ label: c.label, sha: c.comparabilityClassSha256, rows: c.rows.filter(row => row.batteryRunId !== id) }))
      .filter(c => c.rows.length > 0);
  }

  get pairedCandidates(): BatteryPairedCandidate[] {
    return this.pairedClasses.flatMap(c => c.rows.map(row => ({ classLabel: c.label, classSha: c.sha, row })));
  }

  /** This battery run's comparability class: the analysis's, else the battery run's, else its leaderboard row's. */
  get treatmentClassSha(): string | null {
    const id = this.detail?.id;
    return this.analysis?.comparabilityClassSha256
      ?? this.detail?.comparabilityClassSha256
      ?? this.leaderboard?.classes.find(c => c.rows.some(row => row.batteryRunId === id))?.comparabilityClassSha256
      ?? null;
  }

  pairedOptionLabel(row: BenchmarkBatteryLeaderboardRowDto): string {
    return `#${row.batteryRunId} · ${row.testedModelLabel ?? 'unknown model'} · ${formatNumber(row.overallIndex)}`;
  }

  onBaselineSelect(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.compareBaselineId = value === '' ? null : Number(value);
    this.comparison = null;
    this.compareRefusal = null;
    this.pairedResult = null;
    this.pairedIntelligenceVerdict = null;
    this.pairedLoading = false;
    this.pairedError = null;
    this.cdr.markForCheck();
  }

  get pairedKind(): BatteryPairedKind {
    const detail = this.detail;
    const baseline = this.pairedCandidates.find(c => c.row.batteryRunId === this.compareBaselineId);
    if (!detail || !baseline) {
      return 'none';
    }
    const sameModel = detail.testedModelConfigurationId != null
      && detail.testedModelConfigurationId === baseline.row.testedModelConfigurationId;
    const treatmentClass = this.treatmentClassSha;
    if (treatmentClass != null && baseline.classSha === treatmentClass) {
      return sameModel ? 'replicate' : 'model';
    }
    return sameModel ? 'verification' : 'unlikely';
  }

  /** A replicate pair is refused by the server's battery comparability rule, so it is not offered. */
  get canCompare(): boolean {
    return !this.comparing && this.pairedKind !== 'none' && this.pairedKind !== 'replicate';
  }

  /**
   * Pairs this battery run (the treatment) against the chosen baseline: the M7 comparison, which the
   * analysis persists, then the same pair on every measure.
   */
  compare(): void {
    const id = this.detail?.id;
    const baselineId = this.compareBaselineId;
    if (!this.canCompare || id == null || baselineId == null) {
      return;
    }
    const token = this.loadToken;
    this.comparing = true;
    this.comparison = null;
    this.compareRefusal = null;
    this.pairedResult = null;
    this.pairedIntelligenceVerdict = null;
    this.pairedLoading = false;
    this.pairedError = null;
    this.cdr.markForCheck();
    this.benchmarkService.analyseBatteryRun(id, baselineId).subscribe({
      next: (analysis) => {
        if (token !== this.loadToken) return;
        this.comparing = false;
        this.comparison = analysis;
        this.analysis = analysis;
        this.cdr.markForCheck();
        this.comparePairedMeasures(id, baselineId, token);
      },
      error: (err) => {
        if (token !== this.loadToken) return;
        this.comparing = false;
        this.compareRefusal = httpErrorText(err, 'The two battery results could not be compared.');
        this.cdr.markForCheck();
      }
    });
  }

  get comparisonResult(): BenchmarkBatteryComparison | null {
    return this.comparison?.comparison ?? null;
  }

  /** The dimension, speed and cost rows for the pair just compared; a refusal leaves the M7 result shown. */
  private comparePairedMeasures(id: number, baselineId: number, token: number): void {
    this.pairedLoading = true;
    this.cdr.markForCheck();
    this.benchmarkService.getBatteryPairedComparison(id, baselineId).subscribe({
      next: (result) => {
        if (token !== this.loadToken || this.compareBaselineId !== baselineId) return;
        this.pairedLoading = false;
        this.pairedResult = result;
        this.pairedIntelligenceVerdict = this.intelligenceVerdictOf(result);
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (token !== this.loadToken || this.compareBaselineId !== baselineId) return;
        this.pairedLoading = false;
        this.pairedError = pairedErrorText(err, 'The other measures could not be tested.');
        this.cdr.markForCheck();
      }
    });
  }

  /** The Intelligence verdict of a paired result, beside the M7 headline: a word and a shape. */
  private intelligenceVerdictOf(result: BenchmarkPairComparisonDto): { text: string; shape: VerdictShape } | null {
    const measure = result.measures.find(candidate => candidate.category === 'Intelligence');
    if (!measure) {
      return null;
    }
    const pair = findPair(measure, null, null);
    return { text: verdictText(pair), shape: verdictShape(pair, measure) };
  }
}
