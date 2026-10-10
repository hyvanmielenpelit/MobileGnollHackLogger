import { AfterViewInit, Component, ChangeDetectorRef, ViewChild, ElementRef, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  AdminBenchmarkService,
  BenchmarkRunSummaryDto,
  BenchmarkScoringProfileDto,
  SameProviderWarningDto,
  BENCHMARK_SECOND_OPINION_MODES,
  BenchmarkBatteryReusePreviewSlotDto,
  BoardFactsCheckDto,
  BoardFactIssueDto,
  BenchmarkModelBatchFindingDto,
  BenchmarkModelBatchLimitsDto,
  BenchmarkModelBatchRunDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { BenchmarkCompletionSoundKind, BenchmarkCompletionSoundService } from '../../../services/benchmark-completion-sound.service';
import {
  ModelPickerComponent,
  ModelPickerOption
} from '../../../shared/model-picker/model-picker.component';
import { ModelMultiPickerComponent } from '../../../shared/model-picker/model-multi-picker.component';
import { ReorderableListComponent, ReorderableListItem } from '../../../shared/reorderable-list/reorderable-list.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { ensureOverlayPolyfills } from '../../../utils/polyfills.util';
import { reportWriterRefusal, reportWriterWarning } from '../run-ai-reports/report-writer-policy';
import {
  formatCostAmount,
  formatStatus,
  formatElapsed,
  refusalText,
  DELIBERATING_THINKING_LEVELS,
  INTERACTIVE_SPEED_TARGET_MAX_MS,
  MISSING_BOARD_QUOTE_LIST_CAP
} from '../benchmark-run-format';
import { BenchmarkLauncherRunMode, BenchmarkModelBatchOrderChoice } from '../benchmark.models';
import {
  ModelBatchAcknowledgment,
  ModelBatchReadinessComponent,
  modelBatchFindingCode
} from './model-batch-readiness/model-batch-readiness.component';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { BenchmarkLauncherRole, BenchmarkLauncherState } from '../state/benchmark-launcher.state';
import { formatCatalogDate } from '../../../shared/model-availability/model-availability';
import { BenchmarkDifficultyJobService } from '../state/benchmark-difficulty-job.service';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import { batteryAwaitsPostRun, batteryPostRunWork } from '../batteries/battery.models';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** A launcher field a model batch finding can point at, for its inline line and Go to field. */
export type ModelBatchField =
  | 'models' | 'target' | 'profile' | 'responseStyle' | 'sourceCodeReferences'
  | 'assessor' | 'coAssessor' | 'secondOpinion' | 'claimVerifier' | 'reportWriter'
  | 'runs' | 'allowCapWait' | 'order';

/**
 * The server's `field` names, lower-cased with everything but letters removed, to the launcher's
 * fields. The server sends `models`, `assessor`, `coAssessor`, `reader`, `verifier`, `reportWriter`,
 * `profile`, `responseStyle`, `sourceReferences`, `runsPerModel`, `order`, `capWait` and `target`.
 */
const MODEL_BATCH_FIELD_ALIASES: Readonly<Record<string, ModelBatchField>> = {
  models: 'models',
  target: 'target',
  profile: 'profile',
  responsestyle: 'responseStyle',
  sourcereferences: 'sourceCodeReferences',
  assessor: 'assessor',
  coassessor: 'coAssessor',
  reader: 'secondOpinion',
  verifier: 'claimVerifier',
  reportwriter: 'reportWriter',
  runspermodel: 'runs',
  capwait: 'allowCapWait',
  order: 'order'
};

/** A projection basis in words. */
const MODEL_BATCH_BASIS_TEXT: Readonly<Record<string, string>> = {
  OwnRuns: 'its own recent runs on this target',
  Mixed: 'partly its own recent runs, partly the target\'s mean',
  TargetMean: 'the target\'s mean over other models: it has no run of its own here',
  None: 'no basis: neither it nor the target has a completed run'
};

/** The launcher field a finding's `field` names, or null when it names none the launcher shows. */
export function modelBatchFieldOf(field: string | null | undefined): ModelBatchField | null {
  if (!field) return null;
  return MODEL_BATCH_FIELD_ALIASES[field.toLowerCase().replace(/[^a-z]/g, '')] ?? null;
}

/** The short note a selected candidate's chip carries for a warning about it, by code. */
const MODEL_BATCH_CHIP_NOTES: Readonly<Record<string, string>> = {
  'MB-W02': 'Checks its own answers',
  'MB-W05': 'Settings differ',
  'MB-W06': 'Selected twice',
  'MB-W08': 'Same provider as the report writer',
  'MB-W10': 'Same provider as the assessor'
};

/** The id of each role's availability line, `bm<Role>ModelAvailability`, beside its `bm<Role>ModelHint`. */
const ROLE_AVAILABILITY_IDS: Readonly<Record<BenchmarkLauncherRole, string>> = {
  tested: 'bmTestedModelAvailability',
  assessor: 'bmAssessorModelAvailability',
  coAssessor: 'bmCoAssessorModelAvailability',
  secondOpinion: 'bmSecondOpinionModelAvailability',
  claimVerifier: 'bmClaimVerifierModelAvailability',
  reportWriter: 'bmReportWriterModelAvailability'
};

/** What a role's field says about its configuration's place in the model catalog. */
export interface LauncherAvailabilityLine {
  /** `retired` is a refusal that holds Start back; `notInCatalog` an advisory. */
  status: 'retired' | 'notInCatalog';
  text: string;
  configId: number;
  lineId: string;
}

/** The Run Benchmark sub-tab: the launcher, the active run, series, battery and model batch banners, and the start dialogs. */
@Component({
  selector: 'app-benchmark-run-tab',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, FormsModule, ModelPickerComponent, ModelMultiPickerComponent, ReorderableListComponent,
    InfoTipComponent, ModelBatchReadinessComponent
  ],
  templateUrl: './benchmark-run-tab.component.html',
  styleUrls: ['./benchmark-run-tab.component.scss']
})
export class BenchmarkRunTabComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly router = inject(Router);
  private readonly viewSync = inject(BenchmarkViewSync);
  readonly bridge = inject(BenchmarkShellBridge);
  readonly workspace = inject(BenchmarkWorkspaceStore);
  readonly launcher = inject(BenchmarkLauncherState);
  readonly difficulty = inject(BenchmarkDifficultyJobService);
  readonly monitor = inject(BenchmarkActiveRunMonitor);
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);
  private completionSoundService = inject(BenchmarkCompletionSoundService);
  readonly secondOpinionModeOptions = BENCHMARK_SECOND_OPINION_MODES;
  readonly formatStatus = formatStatus;
  readonly formatElapsed = formatElapsed;
  readonly formatCostAmount = formatCostAmount;

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
      this.focusBatchModelsIfPending();
    });
    // A start that ends while this tab is not shown gets no dialog; the run simply does not start.
    this.monitor.startOutcome$.pipe(takeUntilDestroyed()).subscribe(outcome => {
      if (outcome.kind === 'started') {
        this.sameProviderDialog?.nativeElement.close();
        this.sameProviderWarning = null;
      } else {
        this.showSameProviderDialog(outcome.warning);
      }
    });
  }

  ngOnInit(): void {
    // The model batch confirmation's close button carries an interest-triggered tooltip.
    ensureOverlayPolyfills();
  }

  ngAfterViewInit(): void {
    this.focusBatchModelsIfPending();
  }

  ngOnDestroy(): void {
    // The restore notes are shown on one visit of the Run Benchmark sub-tab only.
    this.launcher.clearRestoreNotes();
  }

  /**
   * The line under a role's picker while its selected configuration needs attention: a refusal for a
   * model removed from the catalog, an advisory for a model the catalog does not list; else null.
   */
  roleAvailability(role: BenchmarkLauncherRole): LauncherAvailabilityLine | null {
    const config = this.launcher.selectedConfigFor(role);
    const availability = config?.modelAvailability;
    if (!config || !availability) return null;
    const modelId = config.modelId || config.displayName || `configuration #${config.id}`;
    const lineId = ROLE_AVAILABILITY_IDS[role];
    if (availability.status === 'retired') {
      const name = config.displayName || config.modelId || `Configuration #${config.id}`;
      const date = formatCatalogDate(availability.retiredOn);
      const text = date
        ? `${name} uses ${modelId}, which was removed from the model catalog on ${date}.`
        : `${name} uses ${modelId}, which was removed from the model catalog.`;
      return { status: 'retired', text, configId: config.id, lineId };
    }
    if (availability.status === 'notInCatalog') {
      const text = `${modelId} isn't in the model catalog; its cost will be reported as unknown unless it has a custom price.`;
      return { status: 'notInCatalog', text, configId: config.id, lineId };
    }
    return null;
  }

  /** `base` with the role's availability line added while it shows. */
  describedWithAvailability(base: string, role: BenchmarkLauncherRole): string {
    return this.roleAvailability(role) ? `${base} ${ROLE_AVAILABILITY_IDS[role]}` : base;
  }

  /** Resolve in System Configs: opens System Configs with the configuration's resolution dialog. */
  resolveInSystemConfigs(configId: number): void {
    void this.router.navigate(['/admin'], { queryParams: { tab: 'configs', resolveConfig: configId } });
  }

  /**
   * Focuses the Models Under Test picker once, after a Chat Consistency suggestion set up a model
   * batch; nothing while the picker is not shown yet.
   */
  private focusBatchModelsIfPending(): void {
    if (!this.launcher.batchModelsFocusPending || !this.launcher.isModelBatch) return;
    const control = this.host.nativeElement.querySelector<HTMLElement>('.batch-models-picker .selector-trigger');
    if (!control) return;
    this.launcher.batchModelsFocusPending = false;
    control.scrollIntoView({ block: 'center' });
    control.focus();
  }

  /** The Models Under Test hint: a suggestion's own while fewer than two models are chosen, else the default. */
  get batchModelsHintText(): string {
    const hint = this.launcher.batchModelsHint;
    return hint && this.launcher.batchModelKeys.length < 2 ? hint : 'Choose two or more models.';
  }

  @ViewChild('sameProviderDialog') sameProviderDialog!: ElementRef<HTMLDialogElement>;

  /** Shows the same-provider dialog, or switches its content when a second role's prompt follows the first. */
  private showSameProviderDialog(warning: SameProviderWarningDto): void {
    this.sameProviderWarning = warning;
    const dialog = this.sameProviderDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  get candidateResponseStyleHint(): string {
    return this.launcher.candidateVerboseMode
      ? 'Only Accuracy stays comparable with concise runs; use it to tell prompt gaps from model gaps.'
      : 'Matches the production chat; comparable with earlier runs.';
  }

  sameProviderWarning: SameProviderWarningDto | null = null;

  /** Always-rendered status line beside the two checkboxes: whichever of the two has something to say. */
  get completionSignalsStatusText(): string {
    return [this.monitor.completionSoundStatus, this.monitor.completionNotificationStatus].filter((s): s is string => !!s).join(' ');
  }

  get selectedTestedModel(): SystemAiConfigDto | undefined {
    return this.workspace.benchmarkCapableConfigs.find(c => c.id === this.launcher.testedConfigId);
  }

  get selectedAssessorModel(): SystemAiConfigDto | undefined {
    return this.workspace.benchmarkCapableConfigs.find(c => c.id === this.launcher.assessorConfigId);
  }

  get selectedSecondOpinionModel(): SystemAiConfigDto | undefined {
    return this.workspace.benchmarkCapableConfigs.find(c => c.id === this.launcher.secondOpinionConfigId);
  }

  get selectedClaimVerifierModel(): SystemAiConfigDto | undefined {
    return this.workspace.benchmarkCapableConfigs.find(c => c.id === this.launcher.claimVerifierConfigId);
  }

  get selectedCoAssessorModel(): SystemAiConfigDto | undefined {
    return this.workspace.benchmarkCapableConfigs.find(c => c.id === this.launcher.coAssessorConfigId);
  }

  get selectedReportWriterModel(): SystemAiConfigDto | undefined {
    return this.workspace.benchmarkCapableConfigs.find(c => c.id === this.launcher.reportWriterConfigId);
  }

  /** Provider and model id, as the server's IsSameModel compares a candidate with a panel member. */
  private static sameModel(a: SystemAiConfigDto | undefined, b: SystemAiConfigDto | undefined): boolean {
    return !!a && !!b &&
      BenchmarkLauncherState.sameProvider(a.provider, b.provider) &&
      !!a.modelId && !!b.modelId &&
      a.modelId.trim().toLowerCase() === b.modelId.trim().toLowerCase();
  }

  /**
   * Mirrors the server's refusal: the two panel members must come from different providers. In
   * batch mode the server's findings say this and every other candidate-dependent advisory instead.
   */
  get showCoAssessorSameProviderAdvisory(): boolean {
    return !this.launcher.isModelBatch && this.launcher.isPanelLaunch &&
      BenchmarkLauncherState.sameProvider(this.selectedAssessorModel?.provider, this.selectedCoAssessorModel?.provider);
  }

  /** Mirrors the server's refusal: neither panel member may be the model under test. */
  get showCoAssessorCandidateAdvisory(): boolean {
    const candidate = this.selectedTestedModel;
    return !this.launcher.isModelBatch && this.launcher.isPanelLaunch &&
      (BenchmarkRunTabComponent.sameModel(candidate, this.selectedAssessorModel) ||
        BenchmarkRunTabComponent.sameModel(candidate, this.selectedCoAssessorModel));
  }

  /** Why the server would refuse this panel run, or '' when it would not. */
  get panelLaunchRefusal(): string {
    if (this.showCoAssessorSameProviderAdvisory) {
      return 'The assessor and the co-assessor must come from different providers.';
    }
    if (this.showCoAssessorCandidateAdvisory) {
      return 'Neither panel member may be the model under test; choose another model for the panel.';
    }
    return '';
  }

  /**
   * Mirrors the server's report-writer refusals: an invalid configuration, or the model under test
   * itself. Holds Start back. Empty when there is no writer or nothing to refuse.
   */
  get reportWriterLaunchRefusal(): string {
    if (this.launcher.isModelBatch) return '';
    return reportWriterRefusal(this.selectedReportWriterModel, this.selectedTestedModel);
  }

  /**
   * A writer from the model under test's provider: advisory, Start stays available and the server
   * asks for the acknowledgment. Empty when there is no writer, it is refused, or its provider differs.
   */
  get reportWriterLaunchWarning(): string {
    if (this.launcher.isModelBatch) return '';
    return reportWriterWarning(this.selectedReportWriterModel, this.selectedTestedModel);
  }

  /**
   * The roles a panel run's reference reader or claim verifier shares a provider with. Advisory:
   * a non-scoring role is most useful from a family that is neither a candidate nor a panel member.
   */
  private sharedFamilyRoles(provider: string | null | undefined): string[] {
    if (this.launcher.isModelBatch || !this.launcher.isPanelLaunch || !provider) return [];
    const roles: [string, string | null | undefined][] = [
      ['the model under test', this.selectedTestedModel?.provider],
      ['panel member A', this.selectedAssessorModel?.provider],
      ['panel member B', this.selectedCoAssessorModel?.provider]
    ];
    return roles.filter(([, p]) => BenchmarkLauncherState.sameProvider(provider, p)).map(([role]) => role);
  }

  get referenceReaderSharedFamilyRoles(): string[] {
    return this.launcher.secondOpinionConfigId == null ? [] : this.sharedFamilyRoles(this.selectedSecondOpinionModel?.provider);
  }

  get claimVerifierSharedFamilyRoles(): string[] {
    return this.launcher.claimVerifierConfigId == null ? [] : this.sharedFamilyRoles(this.selectedClaimVerifierModel?.provider);
  }

  /**
   * The verifier is the candidate model itself. Tools supply the evidence rather than the model's
   * memory, so this is not worthless — but it is the weakest available pairing.
   */
  get showClaimVerifierCandidateAdvisory(): boolean {
    return !this.launcher.isModelBatch &&
      this.launcher.claimVerifierConfigId != null &&
      this.launcher.testedConfigId != null &&
      this.launcher.claimVerifierConfigId === this.launcher.testedConfigId;
  }

  get secondOpinionModeDisabled(): boolean {
    return this.launcher.secondOpinionConfigId == null || this.launcher.isPanelLaunch;
  }

  get secondOpinionModeHint(): string {
    if (this.launcher.isPanelLaunch) {
      return 'In a panel run coverage is fixed: the reference reader reads every answer, blind.';
    }
    if (this.secondOpinionModeDisabled) {
      return 'Choose a second reader to set its coverage.';
    }
    return this.secondOpinionModeOptions.find(o => o.value === this.launcher.secondOpinionMode)?.hint ?? '';
  }

  /** The second opinion is the assessor's own model, so it will mostly confirm its own verdict. */
  get showSecondOpinionSameModelNote(): boolean {
    return !!this.selectedSecondOpinionModel && this.selectedSecondOpinionModel.id === this.launcher.assessorConfigId;
  }

  /**
   * Both graders from one provider. The second verdict is still worth having, but it is a weaker
   * check than a cross-provider one: two models from one family share training data and failure
   * modes, and can agree for reasons that have nothing to do with the answer.
   */
  get showAssessorPairingAdvisory(): boolean {
    // A panel run names every shared family in referenceReaderSharedFamilyRoles instead.
    if (this.launcher.isPanelLaunch) return false;
    const assessor = this.selectedAssessorModel?.provider;
    const second = this.selectedSecondOpinionModel?.provider;
    return !!assessor && !!second && assessor.toLowerCase() === second.toLowerCase();
  }

  /**
   * The assessor differs from the one that graded this suite's last completed run. A suite's runs
   * are comparable to each other only while the grader is the same one, so this fires on exactly
   * the deliberate promotion the staged assessor migration calls for — which is when it should.
   */
  get showAssessorChangeAdvisory(): boolean {
    const previous = this.launcher.lastAssessor?.assessorModelConfigurationId;
    return previous != null && this.launcher.assessorConfigId != null && previous !== this.launcher.assessorConfigId;
  }

  /** Stores the launcher's settings as they now stand, and in batch mode asks for them to be checked. */
  private rememberSettings(): void {
    this.launcher.persistRunSettings();
    this.launcher.requestPreflight();
  }

  onSelectedSuiteChanged(): void {
    this.launcher.loadLastAssessor();
    this.rememberSettings();
  }

  selectTestedModel(config: SystemAiConfigDto | null) {
    if (!config) return;
    this.launcher.testedConfigId = config.id;
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  selectAssessorModel(config: SystemAiConfigDto | null) {
    if (!config) return;
    this.launcher.assessorConfigId = config.id;
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  selectSecondOpinionModel(config: SystemAiConfigDto | null) {
    this.launcher.secondOpinionConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  selectCoAssessorModel(config: SystemAiConfigDto | null) {
    this.launcher.coAssessorConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  selectClaimVerifierModel(config: SystemAiConfigDto | null) {
    this.launcher.claimVerifierConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  selectReportWriterModel(config: SystemAiConfigDto | null) {
    this.launcher.reportWriterConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  formatProfileOption(profile: BenchmarkScoringProfileDto): string {
    const cleanName = (profile.name || '').replace(/\s*\(Default\)$/i, '').trim();
    return profile.isDefault ? `${cleanName} (Default)` : cleanName;
  }

  startBenchmark(acknowledgeSameProvider: boolean = false, boardQuotesAcknowledged: boolean = false) {
    if (!this.canStartRun || this.launcher.launchSuiteId == null || this.launcher.testedConfigId == null || this.launcher.assessorConfigId == null) return;

    this.monitor.armCompletionSignalsFromGesture();

    if (acknowledgeSameProvider) {
      // Without a role recorded by the dialog, the acknowledgment is the assessor's.
      if (!this.launcher.launchAcknowledgments.assessor && !this.launcher.launchAcknowledgments.reportWriter) {
        this.launcher.launchAcknowledgments.assessor = true;
      }
    } else if (!boardQuotesAcknowledged) {
      this.launcher.launchAcknowledgments = { assessor: false, reportWriter: false };
    }

    // A same-provider acknowledgement follows a request that already passed this gate. A battery
    // run is not gated here: it spans several suites.
    if (!this.launcher.isBatteryTarget && this.launcher.selectedSuiteId != null
        && !acknowledgeSameProvider && !boardQuotesAcknowledged && this.launcher.selectedSuite?.gameSnapshotId != null) {
      this.checkBoardQuotesBeforeStart(this.launcher.selectedSuiteId);
      return;
    }
    this.monitor.sendStartRequest();
  }

  @ViewChild('boardQuoteWarningDialog') boardQuoteWarningDialog?: ElementRef<HTMLDialogElement>;

  /** The suite's BOARD FACTS quote check, fetched when Start is pressed; set only while it reports missing quotes. */
  launchBoardFactsCheck: BoardFactsCheckDto | null = null;

  get launchMissingBoardQuotes(): BoardFactIssueDto[] {
    return (this.launchBoardFactsCheck?.missingLiterals ?? [])
      .slice(0, MISSING_BOARD_QUOTE_LIST_CAP);
  }

  get launchMissingBoardQuotesOverflow(): number {
    const total = this.launchBoardFactsCheck?.missingLiterals.length ?? 0;
    return Math.max(0, total - MISSING_BOARD_QUOTE_LIST_CAP);
  }

  /**
   * Advisory: missing quotes open a warning the operator may acknowledge, and a failed check
   * starts the run as if it were clean. Nothing here refuses a run.
   */
  private checkBoardQuotesBeforeStart(suiteId: number): void {
    this.monitor.startingRun = true;
    this.monitor.runErrorMessage = null;
    this.benchmarkService.getBoardFactsCheck(suiteId).subscribe({
      next: (check) => {
        if (check && check.missingLiterals.length > 0) {
          this.monitor.startingRun = false;
          this.launchBoardFactsCheck = check;
          this.cdr.detectChanges();
          this.boardQuoteWarningDialog?.nativeElement.showModal();
          return;
        }
        this.monitor.sendStartRequest();
      },
      error: (err) => {
        console.warn('Board facts check before start failed; starting without it', err);
        this.monitor.sendStartRequest();
      }
    });
  }

  closeBoardQuoteWarningDialog(): void {
    this.boardQuoteWarningDialog?.nativeElement.close();
    this.launchBoardFactsCheck = null;
  }

  confirmBoardQuoteWarningRun(): void {
    this.closeBoardQuoteWarningDialog();
    this.startBenchmark(false, true);
  }

  /**
   * True once the operator has asked for more than one run of a single suite, which is what reveals
   * the series projection. A battery and a model batch have projections of their own.
   */
  get isMultiRunRequested(): boolean {
    return !this.launcher.isModelBatch && !this.launcher.isBatteryTarget && this.launcher.effectiveRunCount > 1;
  }

  /**
   * The suite's recent mean run duration, in milliseconds, over its completed runs in the loaded
   * history. Null when the history holds none: a projection with no basis is worse than no
   * projection, because it looks like a measurement.
   */
  get recentMeanRunDurationMs(): number | null {
    const runs = this.completedRunsOfSelectedSuite;
    if (runs.length === 0) return null;
    const total = runs.reduce((sum, r) => sum + (r.totalDurationMs || r.totalAnswerDurationMs || 0), 0);
    return total > 0 ? Math.round(total / runs.length) : null;
  }

  /** The same basis for money: the mean estimated cost of the suite's recent completed runs. */
  get recentMeanRunCost(): number | null {
    const priced = this.completedRunsOfSelectedSuite.filter(r => r.estimatedCost != null);
    if (priced.length === 0) return null;
    return priced.reduce((sum, r) => sum + (r.estimatedCost ?? 0), 0) / priced.length;
  }

  /**
   * Completed runs of the selected suite, newest first, capped at five. Five rather than all of
   * them because a projection should describe the instrument as it is now, and a run from before a
   * model change says nothing useful about how long the next one takes.
   */
  private get completedRunsOfSelectedSuite(): BenchmarkRunSummaryDto[] {
    return this.completedRunsOfSuite(this.launcher.selectedSuiteId);
  }

  /** Completed runs of one suite, newest first, capped at five; the basis of every projection. */
  private completedRunsOfSuite(suiteId: number | null | undefined): BenchmarkRunSummaryDto[] {
    if (suiteId == null) return [];
    return this.workspace.historyRuns
      .filter(r => r.benchmarkSuiteId === suiteId)
      .filter(r => {
        const s = formatStatus(r.status);
        return s === 'Completed' || s === 'CompletedWithErrors' || s === 'CompletedWithLimits';
      })
      .slice(0, 5);
  }

  /** RunCount × the suite's recent mean run duration, or null when there is nothing to project from. */
  get projectedSeriesDurationLabel(): string | null {
    const mean = this.recentMeanRunDurationMs;
    if (mean == null) return null;
    return formatElapsed(mean * this.launcher.effectiveRunCount);
  }

  /** RunCount × the suite's recent mean run cost. Formatted like every other cost on this screen. */
  get projectedSeriesCostLabel(): string | null {
    const mean = this.recentMeanRunCost;
    if (mean == null) return null;
    return formatCostAmount(mean * this.launcher.effectiveRunCount);
  }

  /**
   * Whether the requested series exceeds what the rolling 24-hour window still allows. Advisory: the
   * guard is re-checked per member, and with AllowCapWait a series that outruns the window pauses
   * rather than failing.
   */
  get seriesExceedsDailyHeadroom(): boolean {
    const headroom = this.workspace.runLimits?.remainingDailyHeadroom;
    return headroom != null && this.launcher.effectiveRunCount > headroom;
  }

  /** The Run Target radios' change handler. */
  setRunTarget(kind: 'suite' | 'battery'): void {
    this.launcher.runTargetKind = kind;
    if (kind === 'battery' && this.launcher.selectedBatteryId == null) {
      this.launcher.selectedBatteryId = this.launcher.runnableBatteries[0]?.id ?? null;
    }
    this.launcher.clampRunCountToTarget();
    this.launcher.refreshReusePreview();
    this.rememberSettings();
    this.cdr.detectChanges();
  }

  onSelectedBatteryChanged(): void {
    this.launcher.clampRunCountToTarget();
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  // --- Run Target: reusing earlier runs ---

  /** The Reuse earlier runs checkbox's change handler. */
  onReuseEarlierRunsChange(checked: boolean): void {
    this.launcher.reuseEarlierRuns = checked;
    this.launcher.refreshReusePreview();
  }

  /** A launcher field that shapes the battery request changed: Runs per Suite, the profile, the response style. */
  onReuseInputsChanged(): void {
    this.launcher.refreshReusePreview();
    this.rememberSettings();
  }

  /** The slots no earlier run fills, each with the reason. */
  get reuseUnfilledSlots(): BenchmarkBatteryReusePreviewSlotDto[] {
    return (this.launcher.activeReusePreview?.slots ?? []).filter(s => s.runId == null);
  }

  /** The battery projection's reuse line. */
  get reuseProjectionText(): string {
    if (this.launcher.reusePreviewLoading) {
      return 'Checking which earlier runs can be reused…';
    }
    if (this.launcher.reusePreviewError) {
      return `The reuse of earlier runs could not be previewed: ${this.launcher.reusePreviewError}`;
    }
    const preview = this.launcher.activeReusePreview;
    if (!preview) {
      return 'Choose the battery and the models to see which earlier runs can be reused.';
    }
    if (preview.reusedCount === 0) {
      return `No earlier run qualifies; launching ${preview.launchCount}.`;
    }
    const ids = preview.attach.map(a => `#${a.runId}`).join(', ');
    return `Reusing ${preview.reusedCount} earlier ${preview.reusedCount === 1 ? 'run' : 'runs'} (${ids}); `
      + `launching ${preview.launchCount}.`;
  }

  /** The launches the battery run plans: K × R, less the slots earlier runs fill. */
  get batteryRunsToLaunch(): number {
    const preview = this.launcher.activeReusePreview;
    return preview ? preview.launchCount : this.batteryLaunchCount;
  }

  /** K × R: the launches the battery run plans. */
  get batteryLaunchCount(): number {
    return (this.launcher.selectedBattery?.suites.length ?? 0) * this.launcher.effectiveRunCount;
  }

  /** The selected battery's suites with the declared scheme's normalized weights, for the select's info tip. */
  get selectedBatteryWeightRows(): { name: string; percent: number | null }[] {
    const battery = this.launcher.selectedBattery;
    if (!battery) return [];
    const declared = battery.weightPreviews?.find(p => p.declared);
    return battery.suites.map((suite, i) => ({
      name: suite.suiteName,
      percent: declared && declared.weights.length === battery.suites.length ? declared.weights[i] * 100 : null
    }));
  }

  get selectedBatterySchemeLabel(): string {
    return this.launcher.selectedBattery?.weightingSchemeLabel ?? '';
  }

  /** The battery's suites whose questions are not all difficulty-assessed; the server refuses them. */
  get selectedBatteryUnassessedSuites(): string[] {
    return (this.launcher.selectedBattery?.suites ?? [])
      .filter(s => !s.difficultyFullyAssessed)
      .map(s => s.suiteName);
  }

  /**
   * The projection's sum of each launched run's suite's recent mean run duration: each suite × R,
   * or, with a reuse preview, the slots no earlier run fills. Null unless every such suite has a basis.
   */
  get projectedBatteryDurationLabel(): string | null {
    const total = this.sumOverBatteryLaunches(id => this.meanRunDurationMsOf(id));
    return total == null ? null : formatElapsed(total);
  }

  /** The same sum for money. */
  get projectedBatteryCostLabel(): string | null {
    const total = this.sumOverBatteryLaunches(id => this.meanRunCostOf(id));
    return total == null ? null : formatCostAmount(total);
  }

  private sumOverBatteryLaunches(perSuite: (suiteId: number) => number | null): number | null {
    const preview = this.launcher.activeReusePreview;
    if (!preview) {
      const total = this.sumOverBatterySuites(perSuite);
      return total == null ? null : total * this.launcher.effectiveRunCount;
    }
    let total = 0;
    for (const slot of preview.slots.filter(s => s.runId == null)) {
      const value = perSuite(slot.suiteId);
      if (value == null) return null;
      total += value;
    }
    return total;
  }

  private sumOverBatterySuites(perSuite: (suiteId: number) => number | null): number | null {
    const suites = this.launcher.selectedBattery?.suites ?? [];
    if (suites.length === 0) return null;
    let total = 0;
    for (const suite of suites) {
      const value = suite.suiteId == null ? null : perSuite(suite.suiteId);
      if (value == null) return null;
      total += value;
    }
    return total;
  }

  private meanRunDurationMsOf(suiteId: number): number | null {
    const runs = this.completedRunsOfSuite(suiteId);
    if (runs.length === 0) return null;
    const total = runs.reduce((sum, r) => sum + (r.totalDurationMs || r.totalAnswerDurationMs || 0), 0);
    return total > 0 ? Math.round(total / runs.length) : null;
  }

  private meanRunCostOf(suiteId: number): number | null {
    const priced = this.completedRunsOfSuite(suiteId).filter(r => r.estimatedCost != null);
    if (priced.length === 0) return null;
    return priced.reduce((sum, r) => sum + (r.estimatedCost ?? 0), 0) / priced.length;
  }

  /** Whether the battery asks for more launches than the rolling 24-hour window still allows. */
  get batteryExceedsDailyHeadroom(): boolean {
    const headroom = this.workspace.runLimits?.remainingDailyHeadroom;
    return headroom != null && this.batteryRunsToLaunch > headroom;
  }

  /** More launches than the daily cap itself: admitted only with Allow cap wait. */
  get batteryExceedsDailyCap(): boolean {
    const cap = this.workspace.runLimits?.maxRunsPerDay;
    return cap != null && cap > 0 && this.batteryRunsToLaunch > cap;
  }

  /** The rolling 24-hour windows the launches need at the daily cap. */
  get batteryDaySpan(): number | null {
    const cap = this.workspace.runLimits?.maxRunsPerDay;
    if (cap == null || cap <= 0 || this.batteryRunsToLaunch === 0) return null;
    return Math.ceil(this.batteryRunsToLaunch / cap);
  }

  /** Why a battery run cannot start yet, or empty; also the Start hint in battery mode. */
  get batteryLaunchRefusal(): string {
    const battery = this.launcher.selectedBattery;
    if (!battery) {
      return this.launcher.runnableBatteries.length === 0
        ? 'Create a battery on the Multi-Suite tab first.'
        : 'Select a battery first.';
    }
    const unassessed = this.selectedBatteryUnassessedSuites;
    if (unassessed.length > 0) {
      return `Assess every question's difficulty in ${unassessed.join(', ')} first.`;
    }
    const maxMembers = this.workspace.runLimits?.maxMembersPerBattery;
    if (maxMembers != null && battery.suites.length > maxMembers) {
      return `This battery has ${battery.suites.length} suites; one battery run may plan at most ${maxMembers} runs.`;
    }
    if (this.batteryExceedsDailyCap && !this.launcher.allowCapWait) {
      return `${this.batteryRunsToLaunch} runs exceed the daily cap of ${this.workspace.runLimits?.maxRunsPerDay}; select Wait when the run cap blocks the next run.`;
    }
    // Start sends the previewed runs, so with reuse on it waits for a preview of what it would send.
    if (this.launcher.reuseEarlierRuns && !this.launcher.activeReusePreview && this.launcher.buildBatteryStartRequest() != null) {
      return this.launcher.reusePreviewError
        ? 'The reuse of earlier runs could not be previewed; clear Reuse earlier runs to start without it.'
        : 'Checking which earlier runs can be reused…';
    }
    return '';
  }

  /**
   * Whether the Run Benchmark tab shows the series banner.
   *
   * <p>A banner describing a series that has finished is an alert with nothing to alert about, and
   * it outlives the work by however long the page stays open. `activeSeries` itself is kept — the
   * progress dialog and the run-to-series labelling read it after completion — so this gates the
   * rendering rather than clearing the state.</p>
   *
   * <p>The completed series stays reachable from the Multi-Run Analysis tab, whose group rows carry
   * a Series badge that opens the same dialog. That matters because the dialog is the only place
   * either diagnostics capture can be copied from.</p>
   */
  get seriesBannerVisible(): boolean {
    return this.monitor.activeSeries != null && !this.monitor.multiRunDialogVisible && !this.monitor.seriesIsFinished
      && !this.modelBatchHoldsBanners;
  }

  get seriesIsWaitingForCap(): boolean {
    return this.monitor.activeSeries?.status === 'WaitingForCap';
  }

  /** *Run n of N* — the count the operator actually watches, rather than a bare percentage. */
  get seriesProgressLabel(): string {
    const series = this.monitor.activeSeries;
    if (!series) return '';
    const current = Math.min(series.completedRunCount + 1, series.requestedRunCount);
    switch (series.status) {
      case 'WaitingForCap':
        return `Waiting for run cap — ${series.completedRunCount} of ${series.requestedRunCount} runs completed.`;
      case 'Stopped':
        return `Stopped — ${series.stopReasonText || series.stopReason || 'reason not recorded'}. `
          + `${series.completedRunCount} of ${series.requestedRunCount} runs completed. `
          + 'Continue from the progress dialog.';
      case 'Pending':
        return `Launching run 1 of ${series.requestedRunCount}.`;
      case 'Running':
        return `Run ${current} of ${series.requestedRunCount}.`;
      default:
        return `${series.status} — ${series.completedRunCount} of ${series.requestedRunCount} runs completed.`;
    }
  }

  get batteryIsStopped(): boolean {
    return this.monitor.activeBatteryRun?.status === 'Stopped';
  }

  get batteryIsWaitingForCap(): boolean {
    return this.monitor.activeBatteryRun?.status === 'WaitingForCap';
  }

  /**
   * The battery banner shows while the battery run is live, stopped, or still worked on by the server
   * (`postRunWork`), and its dialog is closed. Completed with errors is finished here: it is continued,
   * if at all, from the progress dialog.
   */
  get batteryBannerVisible(): boolean {
    const batteryRun = this.monitor.activeBatteryRun;
    if (batteryRun == null || this.monitor.batteryDialogVisible || this.modelBatchHoldsBanners) {
      return false;
    }
    return this.monitor.batteryIsLive || this.batteryIsStopped || batteryAwaitsPostRun(batteryRun);
  }

  /** *Suite s of K · round r of R*, or the state that replaces it. */
  get batteryProgressLabel(): string {
    const batteryRun = this.monitor.activeBatteryRun;
    if (!batteryRun) return '';
    const suites = `${batteryRun.completedSuiteCount} of ${batteryRun.suiteCount} suites completed`;
    if (!this.monitor.batteryIsLive) {
      switch (batteryPostRunWork(batteryRun)) {
        case 'Repairing':
          return `Re-run in progress — ${suites}.`;
        case 'Analysing':
          return `Computing the battery analysis — ${suites}.`;
        case 'WritingReports':
          return `Writing the AI reports — ${suites}.`;
      }
    }
    switch (batteryRun.status) {
      case 'WaitingForCap':
        return `Waiting for run cap — ${suites}.`;
      case 'Stopped':
        return `Stopped — ${batteryRun.stopReasonText || batteryRun.stopReason || 'reason not recorded'}. ${suites}. `
          + 'Continue from the progress dialog.';
      case 'Pending':
      case 'Running':
        return batteryRun.currentSuitePosition != null
          ? `Suite ${batteryRun.currentSuitePosition} of ${batteryRun.suiteCount}`
            + `${batteryRun.currentSuiteName ? ` (${batteryRun.currentSuiteName})` : ''}`
            + ` · round ${batteryRun.currentRound ?? 1} of ${batteryRun.runsPerSuite}.`
          : `Launching — ${suites}.`;
      case 'CompletedWithErrors':
        return `Completed with errors — ${suites}.`;
      default:
        return `${batteryRun.status} — ${suites}.`;
    }
  }

  closeSameProviderDialog() {
    this.sameProviderDialog?.nativeElement.close();
    this.sameProviderWarning = null;
  }

  /** Acknowledge & Start Run: adds the shown role's acknowledgment to those given in this attempt and sends again. */
  confirmSameProviderRun() {
    if (this.sameProviderWarningIsReportWriter) {
      this.launcher.launchAcknowledgments.reportWriter = true;
    } else {
      this.launcher.launchAcknowledgments.assessor = true;
    }
    this.startBenchmark(true);
  }

  /** The same-provider dialog is about the report writer rather than the assessor. */
  get sameProviderWarningIsReportWriter(): boolean {
    return this.sameProviderWarning?.role === 'reportWriter';
  }

  /** The *Play a sound* checkbox's change handler. */
  onCompletionSoundChanged(): void {
    this.rememberSettings();
  }

  /**
   * The *Test sound* button: plays under this click's user gesture, which also unlocks later
   * programmatic playback on browsers that require one interaction before audio is allowed.
   */
  testCompletionSound(): void {
    this.testSound('complete');
  }

  /** The *Test failure sound* button: the same, with the sound a failed, stopped or erroneous end plays. */
  testFailureSound(): void {
    this.testSound('failed');
  }

  private testSound(kind: BenchmarkCompletionSoundKind): void {
    this.monitor.armCompletionSignalsFromGesture();
    this.monitor.completionSoundStatus = null;
    this.completionSoundService.prime(kind).then(outcome => {
      this.monitor.lastCompletionSoundOutcome = outcome;
      if (outcome === 'blocked') {
        this.monitor.completionSoundStatus = 'Playback was blocked by the browser — press Test sound once to allow it.';
      }
      this.cdr.detectChanges();
    });
  }

  get showProfileFitAdvisory(): boolean {
    if (this.launcher.isModelBatch) return false;
    const thinkingLevel = this.selectedTestedModel?.thinkingLevel;
    const speedTargetMs = this.launcher.selectedScoringProfile?.speedTargetMs;
    if (!thinkingLevel || speedTargetMs == null) return false;

    return DELIBERATING_THINKING_LEVELS.includes(thinkingLevel.toLowerCase()) &&
      speedTargetMs < INTERACTIVE_SPEED_TARGET_MAX_MS;
  }

  get canStartRun(): boolean {
    const target = this.launcher.isBatteryTarget
      ? !this.batteryLaunchRefusal
      : !!this.launcher.selectedSuiteId && !!this.launcher.selectedSuite?.difficultyFullyAssessed;
    return !this.monitor.startingRun &&
      target &&
      !!this.launcher.testedConfigId &&
      !!this.launcher.assessorConfigId &&
      !(this.monitor.activeRunDetail && formatStatus(this.monitor.activeRunDetail.status) === 'Running') &&
      !this.launcher.retiredRoleRefusal() &&
      !this.panelLaunchRefusal &&
      !this.reportWriterLaunchRefusal;
  }

  /**
   * Names the first condition Start Benchmark is waiting on, for the button's aria-disabled hint. Empty
   * once canStartRun is true. The target comes first, then the two required roles, then a role whose
   * model left the catalog, then the panel and report writer refusals.
   */
  get startBenchmarkHint(): string {
    if (this.launcher.isBatteryTarget) {
      if (this.batteryLaunchRefusal) {
        return this.batteryLaunchRefusal;
      }
    } else if (!this.launcher.selectedSuiteId) {
      return 'Select a question suite first.';
    } else if (!this.launcher.selectedSuite?.difficultyFullyAssessed) {
      return "Assess every question's difficulty first.";
    }
    if (!this.launcher.testedConfigId || !this.launcher.assessorConfigId) {
      return 'Choose a model under test and an assessor.';
    }
    const retired = this.launcher.retiredRoleRefusal();
    if (retired) {
      return `${retired.label} uses a model removed from the catalog. Resolve it in System Configs or choose another model.`;
    }
    if (this.panelLaunchRefusal) {
      return this.panelLaunchRefusal;
    }
    if (this.reportWriterLaunchRefusal) {
      return this.reportWriterLaunchRefusal;
    }
    return '';
  }

  // --- Model batches ---
  //
  // The Models radios switch the launcher between one model under test and a model batch. In batch
  // mode the server's guardrail findings are shown three ways: a field's blocker or warning as one
  // line under it, all of them in the Batch Readiness card above Start, and the acknowledged warnings
  // in the Start confirmation. Nothing here judges a guardrail itself, except the picker's
  // unavailable options (a scoring grader or the report writer), which are derived for immediacy.

  /** The Models radios' change handler. A batch starts with no report writer: each member would get its own documents. */
  setRunMode(mode: BenchmarkLauncherRunMode): void {
    if (this.launcher.runMode === mode) return;
    this.launcher.runMode = mode;
    if (mode === 'batch') {
      this.launcher.reportWriterConfigId = null;
      // A model batch never reuses earlier runs.
      this.launcher.reuseEarlierRuns = false;
      this.launcher.refreshReusePreview();
    }
    this.rememberSettings();
    this.cdr.markForCheck();
  }

  /** The Models Under Test picker's change handler. */
  onBatchModelsChange(keys: readonly (string | number)[]): void {
    this.launcher.setBatchModels(keys.map(k => Number(k)).filter(k => Number.isFinite(k)));
    this.rememberSettings();
  }

  /** The Model order radios' change handler. */
  setBatchOrder(order: BenchmarkModelBatchOrderChoice): void {
    this.launcher.batchOrder = order;
    this.rememberSettings();
    this.cdr.markForCheck();
  }

  /** The As listed order's change handler. */
  onBatchOrderListChange(keys: string[]): void {
    this.launcher.setBatchOrderKeys(keys.map(k => Number(k)));
    this.rememberSettings();
  }

  /** Runs per model, and every other batch field whose change only needs saving and checking. */
  onBatchSettingsChanged(): void {
    this.rememberSettings();
  }

  /** *Wait when the run cap blocks the next run*: decided at each start, so checked but not saved. */
  onAllowCapWaitChanged(): void {
    this.launcher.requestPreflight();
  }

  onFindingAcknowledged(change: ModelBatchAcknowledgment): void {
    this.launcher.setFindingAcknowledged(change.key, change.acknowledged);
    this.cdr.markForCheck();
  }

  private batchPickerCache: {
    source: ModelPickerOption<SystemAiConfigDto>[];
    assessorId: number | null;
    coAssessorId: number | null;
    reportWriterId: number | null;
    findings: BenchmarkModelBatchFindingDto[];
    models: number[];
    options: ModelPickerOption<SystemAiConfigDto>[];
  } | null = null;

  /**
   * The Models Under Test options: every benchmark-capable model, a scoring grader or the report
   * writer unavailable with the reason (MB-B03, MB-B04), and a chosen model that a warning names
   * carrying a short note. Memoized on its inputs, so the picker sees a new array only when one changes.
   */
  get batchPickerOptions(): ModelPickerOption<SystemAiConfigDto>[] {
    const source = this.workspace.benchmarkPickerOptions;
    const launcher = this.launcher;
    const cached = this.batchPickerCache;
    if (cached && cached.source === source && cached.assessorId === launcher.assessorConfigId
        && cached.coAssessorId === launcher.coAssessorConfigId && cached.reportWriterId === launcher.reportWriterConfigId
        && cached.findings === launcher.findings && cached.models === launcher.batchModelKeys) {
      return cached.options;
    }
    const assessor = this.selectedAssessorModel;
    const coAssessor = this.selectedCoAssessorModel;
    const writer = this.selectedReportWriterModel;
    const options = source.map(option => {
      const model = option.model;
      const reason = BenchmarkRunTabComponent.sameModel(model, assessor) || BenchmarkRunTabComponent.sameModel(model, coAssessor)
        ? 'Grades this batch'
        : BenchmarkRunTabComponent.sameModel(model, writer) ? 'Writes its reports' : '';
      const detail = this.batchChipNote(model.id);
      return {
        ...option,
        ...(detail ? { detail } : {}),
        ...(reason ? { disabledReason: reason } : {})
      };
    });
    this.batchPickerCache = {
      source, assessorId: launcher.assessorConfigId, coAssessorId: launcher.coAssessorConfigId,
      reportWriterId: launcher.reportWriterConfigId, findings: launcher.findings, models: launcher.batchModelKeys, options
    };
    return options;
  }

  /** The warnings that name this chosen model, as the short notes its chip shows. */
  private batchChipNote(configId: number): string {
    if (!this.launcher.batchModelKeys.includes(configId)) return '';
    const notes = this.launcher.batchWarnings
      .filter(f => f.modelConfigurationIds?.includes(configId))
      .map(f => MODEL_BATCH_CHIP_NOTES[modelBatchFindingCode(f)])
      .filter((note): note is string => !!note);
    return notes.filter((note, i) => notes.indexOf(note) === i).join(' · ');
  }

  private batchOrderCache: { keys: number[]; source: SystemAiConfigDto[]; items: ReorderableListItem[] } | null = null;

  /** The chosen models in the As listed order, for the reorderable list. Memoized like the picker options. */
  get batchOrderItems(): ReorderableListItem[] {
    const keys = this.launcher.batchOrderKeys;
    const source = this.workspace.stableBenchmarkCapableConfigs;
    const cached = this.batchOrderCache;
    if (cached && cached.keys === keys && cached.source === source) {
      return cached.items;
    }
    const items = keys.map(id => ({ key: String(id), label: this.batchModelName(id) }));
    this.batchOrderCache = { keys, source, items };
    return items;
  }

  /** A configuration's display name, else its model id, else its id. */
  batchModelName(configId: number): string {
    const config = this.workspace.benchmarkCapableConfigs.find(c => c.id === configId);
    return config?.displayName || config?.modelId || `Configuration #${configId}`;
  }

  private batchFieldLinesCache: { findings: BenchmarkModelBatchFindingDto[]; lines: Partial<Record<ModelBatchField, BenchmarkModelBatchFindingDto>> } | null = null;

  /** Each field's inline line: its first blocker, else its first warning. Advice is never inline. */
  get batchFieldLines(): Partial<Record<ModelBatchField, BenchmarkModelBatchFindingDto>> {
    if (!this.launcher.isModelBatch) return {};
    const findings = this.launcher.findings;
    if (this.batchFieldLinesCache?.findings === findings) {
      return this.batchFieldLinesCache.lines;
    }
    const lines: Partial<Record<ModelBatchField, BenchmarkModelBatchFindingDto>> = {};
    for (const severity of ['Blocker', 'Warning'] as const) {
      for (const finding of findings) {
        const field = modelBatchFieldOf(finding.field);
        if (finding.severity === severity && field && !lines[field]) {
          lines[field] = finding;
        }
      }
    }
    this.batchFieldLinesCache = { findings, lines };
    return lines;
  }

  /** The finding shown inline under `field`, if any. */
  batchFieldLine(field: ModelBatchField): BenchmarkModelBatchFindingDto | undefined {
    return this.batchFieldLines[field];
  }

  /** The id of the field's inline line. */
  batchLineId(field: ModelBatchField): string {
    return `mbLine-${field}`;
  }

  /** `base` with the field's inline line added while it shows. */
  describedWithBatchLine(base: string, field: ModelBatchField): string {
    return this.batchFieldLines[field] ? `${base} ${this.batchLineId(field)}`.trim() : base;
  }

  /** Go to field: brings the field's control into view and focuses it. */
  focusBatchField(field: string): void {
    const selector = this.batchFieldSelector(modelBatchFieldOf(field));
    const control = selector ? this.host.nativeElement.querySelector<HTMLElement>(selector) : null;
    if (!control) return;
    control.scrollIntoView({ block: 'center' });
    control.focus();
  }

  private batchFieldSelector(field: ModelBatchField | null): string | null {
    switch (field) {
      case 'models': return '.batch-models-picker .selector-trigger';
      case 'target': return this.launcher.isBatteryTarget ? '#batterySelect' : '#suiteSelect';
      case 'profile': return '#profileSelect';
      case 'responseStyle': return '#candidateResponseStyle';
      case 'sourceCodeReferences': return '#candidateSourceCodeReferences';
      case 'assessor': return '.assessor-model-selector .selector-trigger';
      case 'coAssessor': return '.co-assessor-model-selector .selector-trigger';
      case 'secondOpinion': return '.second-opinion-model-selector .selector-trigger';
      case 'claimVerifier': return '.claim-verifier-model-selector .selector-trigger';
      case 'reportWriter': return '.report-writer-model-selector .selector-trigger';
      case 'runs': return this.launcher.isBatteryTarget ? '#runCountInput' : '#batchRunsPerModelInput';
      case 'allowCapWait': return '#allowCapWaitInput';
      case 'order': return this.launcher.batchOrder === 'asListed' ? '#batchOrderAsListed' : '#batchOrderRandomized';
      default: return null;
    }
  }

  /**
   * The card shows once a model is chosen, and before that only for a blocker that does not depend on
   * the choice (a benchmark already running).
   */
  get batchReadinessVisible(): boolean {
    if (!this.launcher.isModelBatch) return false;
    return this.launcher.batchModelKeys.length > 0
      || this.launcher.batchBlockers.some(f => !['MB-B01', 'MB-B06'].includes(modelBatchFindingCode(f)));
  }

  // --- Model batches: the projection ---

  /** K: the battery's suites, or the one suite. */
  get batchSuiteCount(): number {
    return this.launcher.isBatteryTarget ? (this.launcher.selectedBattery?.suites.length ?? 0) : 1;
  }

  /** L, as the server projects it, else models × K × R. */
  get batchPlannedRunCount(): number {
    return this.launcher.projection?.plannedRunCount
      ?? this.launcher.batchModelKeys.length * this.batchSuiteCount * this.launcher.batchRunsPerMember;
  }

  get batchProjectionLegend(): string {
    const models = this.launcher.batchModelKeys.length;
    const modelsText = `${models} ${models === 1 ? 'model' : 'models'}`;
    const runs = this.launcher.batchRunsPerMember;
    return this.launcher.isBatteryTarget
      ? `Batch Projection (${modelsText} × ${this.batchSuiteCount} suites × ${runs} = ${this.batchPlannedRunCount} runs)`
      : `Batch Projection (${modelsText} × ${runs} = ${this.batchPlannedRunCount} runs)`;
  }

  get batchLimits(): BenchmarkModelBatchLimitsDto | null {
    return this.launcher.projection?.limits ?? null;
  }

  /** The caps and windows the projection names: the batch's own read-out, else the launcher's run limits. */
  get batchWindow(): { remainingDailyHeadroom: number; maxRunsPerDay: number; maxRunsPerHour: number; runsInLast24Hours: number; runsInLastHour: number } | null {
    return this.batchLimits ?? this.workspace.runLimits ?? null;
  }

  get projectedBatchWallLabel(): string | null {
    const ms = this.launcher.projection?.projectedWallMs;
    return ms == null ? null : formatElapsed(ms);
  }

  get projectedBatchCostLabel(): string | null {
    const cost = this.launcher.projection?.projectedCostUsd;
    return cost == null ? null : formatCostAmount(cost);
  }

  // The three alerts below the projection are read from the numbers; the findings MB-B08, MB-W11 and
  // MB-W12 say the same with authority in the readiness card.

  /** More launches than the daily cap itself: admitted only with Wait when the run cap blocks the next run. */
  get batchExceedsDailyCap(): boolean {
    const cap = this.batchWindow?.maxRunsPerDay;
    return cap != null && cap > 0 && this.batchPlannedRunCount > cap;
  }

  /** More launches than the rolling 24-hour window still allows, within the cap. */
  get batchExceedsDailyHeadroom(): boolean {
    if (this.batchExceedsDailyCap) return false;
    const headroom = this.batchWindow?.remainingDailyHeadroom;
    return headroom != null && this.batchPlannedRunCount > headroom;
  }

  /** The rolling 24-hour windows the plan needs, when it needs more than one. */
  get batchDaySpan(): number | null {
    const own = this.batchLimits?.daySpan;
    if (own != null) return own > 1 ? own : null;
    const cap = this.batchWindow?.maxRunsPerDay;
    if (cap == null || cap <= 0) return null;
    const span = Math.ceil(this.batchPlannedRunCount / cap);
    return span > 1 ? span : null;
  }

  /** The least wall time a plan of several windows takes: (windows − 1) × 24 h. */
  get batchMinimumWallLabel(): string | null {
    const own = this.batchLimits?.minimumWallMs;
    if (own != null) return formatElapsed(own);
    const span = this.batchDaySpan;
    return span == null ? null : formatElapsed((span - 1) * 24 * 3600 * 1000);
  }

  /** The projected launch rate is above what the hourly cap still allows this hour. */
  get batchHourlyCapRisk(): boolean {
    const limits = this.batchLimits;
    const rate = limits?.projectedRunsPerHour;
    return limits != null && rate != null && rate > limits.maxRunsPerHour - limits.runsInLastHour;
  }

  /** The per-model basis lines of the projection, each with its model's name. */
  get batchProjectionMembers(): { name: string; cost: string | null; wall: string | null; basis: string }[] {
    return (this.launcher.projection?.members ?? []).map(m => ({
      name: this.batchModelName(m.modelConfigurationId),
      cost: m.projectedCostUsd == null ? null : formatCostAmount(m.projectedCostUsd),
      wall: m.projectedWallMs == null ? null : formatElapsed(m.projectedWallMs),
      basis: MODEL_BATCH_BASIS_TEXT[m.basis] ?? m.basis
    }));
  }

  // --- Model batches: Start ---

  @ViewChild('modelBatchConfirmDialog') modelBatchConfirmDialog?: ElementRef<HTMLDialogElement>;

  /** The start request is in flight. */
  startingModelBatch = false;

  get canStartModelBatch(): boolean {
    return !this.startModelBatchHint;
  }

  /** Names the first condition Start Model Batch waits on, for the button's aria-disabled hint. Empty once it may start. */
  get startModelBatchHint(): string {
    const launcher = this.launcher;
    if (this.startingModelBatch) {
      return 'Starting the model batch…';
    }
    if (launcher.batchModelKeys.length < 2) {
      return 'Choose two or more models.';
    }
    if (launcher.isBatteryTarget) {
      if (!launcher.selectedBattery) {
        return this.batteryLaunchRefusal;
      }
    } else if (!launcher.selectedSuiteId) {
      return 'Select a question suite first.';
    }
    if (!launcher.assessorConfigId) {
      return 'Choose an assessor.';
    }
    if (launcher.preflightError) {
      return `The batch could not be checked: ${launcher.preflightError} It is checked again shortly.`;
    }
    if (launcher.preflightLoading || !launcher.preflightAnswered) {
      return 'Checking the batch…';
    }
    const blocker = launcher.batchBlockers[0];
    if (blocker) {
      return /[.!?]$/.test(blocker.title) ? blocker.title : `${blocker.title}.`;
    }
    const unacknowledged = launcher.unacknowledgedBatchWarnings.length;
    if (unacknowledged > 0) {
      return unacknowledged === 1
        ? 'Acknowledge the warning in Batch Readiness.'
        : `Acknowledge the ${unacknowledged} warnings in Batch Readiness.`;
    }
    return '';
  }

  /** Start Model Batch: shows the plan for confirmation; nothing is sent yet. */
  openModelBatchConfirm(): void {
    if (!this.canStartModelBatch || !this.launcher.buildModelBatchRequest()) return;
    this.cdr.detectChanges();
    this.modelBatchConfirmDialog?.nativeElement.showModal();
  }

  closeModelBatchConfirm(): void {
    this.modelBatchConfirmDialog?.nativeElement.close();
  }

  /**
   * The confirmation's Start Model Batch. Arms both chimes under this gesture, then starts the batch
   * and opens its progress dialog. A refusal carrying findings refreshes the card instead.
   */
  confirmModelBatchStart(): void {
    const req = this.launcher.buildModelBatchRequest();
    if (!req || this.startingModelBatch) return;

    this.monitor.armCompletionSignalsFromGesture();
    this.launcher.persistRunSettings();
    this.startingModelBatch = true;
    this.monitor.runErrorMessage = null;

    this.benchmarkService.startModelBatch(req).subscribe({
      next: (batch: BenchmarkModelBatchRunDto) => {
        this.startingModelBatch = false;
        this.closeModelBatchConfirm();
        // Acknowledgments are given for one start.
        this.launcher.acknowledgedFindingKeys = new Set();
        this.monitor.followModelBatch(batch);
        this.monitor.openModelBatchDialog(batch.id);
        this.workspace.loadHistory();
        this.launcher.requestPreflight();
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.startingModelBatch = false;
        this.closeModelBatchConfirm();
        const findings = err?.error?.findings;
        if (Array.isArray(findings)) {
          this.launcher.applyFindings(findings as BenchmarkModelBatchFindingDto[]);
          this.launcher.preflightAnswered = true;
        } else {
          this.monitor.runErrorMessage = refusalText(err, 'Failed to start the model batch.');
        }
        this.cdr.markForCheck();
      }
    });
  }

  /** The confirmation's run order: the As listed order, or the chosen models under a random order. */
  get batchConfirmModels(): string[] {
    return this.launcher.batchRunOrderIds.map(id => this.batchModelName(id));
  }

  get batchConfirmOrderText(): string {
    return this.launcher.batchOrder === 'asListed'
      ? 'As listed'
      : 'Random order, drawn when the batch starts; the progress dialog shows the order and its seed';
  }

  get batchConfirmTargetText(): string {
    if (this.launcher.isBatteryTarget) {
      const battery = this.launcher.selectedBattery;
      return battery ? `Battery: ${battery.name} (${battery.suites.length} suites)` : 'Battery';
    }
    return `Suite: ${this.launcher.selectedSuite?.name ?? 'none selected'}`;
  }

  /** Every grading role in one line, with its model. */
  get batchConfirmGraders(): string {
    const parts: string[] = [];
    const add = (role: string, model: SystemAiConfigDto | undefined): void => {
      if (model) parts.push(`${role}: ${model.displayName || model.modelId}`);
    };
    add('Assessor', this.selectedAssessorModel);
    add('Co-assessor', this.selectedCoAssessorModel);
    add(this.launcher.isPanelLaunch ? 'Reference reader' : 'Second reader', this.selectedSecondOpinionModel);
    add('Claim verifier', this.selectedClaimVerifierModel);
    add('Report writer', this.selectedReportWriterModel);
    return parts.join(' · ');
  }

  // --- Model batches: the banner ---

  /** A model batch is live or stopped: its banner stands for its members, whose own banners stay hidden. */
  get modelBatchHoldsBanners(): boolean {
    const batch = this.monitor.activeModelBatch;
    return batch != null && (this.monitor.modelBatchIsLive || batch.status === 'Stopped');
  }

  /** The banner's Show Batch Progress. */
  showModelBatchProgress(): void {
    const id = this.monitor.activeModelBatch?.id;
    if (id != null) {
      this.monitor.openModelBatchDialog(id);
    }
  }

  /** The batch banner shows while the batch is live or stopped and its dialog is closed. */
  get modelBatchBannerVisible(): boolean {
    return this.modelBatchHoldsBanners && !this.monitor.modelBatchDialogVisible;
  }

  /** *Model k of M: {model}*, or the state that replaces it. */
  get modelBatchProgressLabel(): string {
    const batch = this.monitor.activeModelBatch;
    if (!batch) return '';
    const total = batch.requestedMemberCount || batch.members.length;
    const index = batch.currentMemberIndex;
    const member = index != null ? batch.members[index] : undefined;
    const done = `${batch.completedMemberCount} of ${total} models completed`;
    switch (batch.status) {
      case 'Stopped':
        return `Stopped — ${batch.stopReasonText || batch.stopReason || 'reason not recorded'}. ${done}. `
          + 'Continue from the progress dialog.';
      case 'WaitingForCap':
        return `Waiting for run cap — ${done}.`;
      default:
        return member
          ? `Model ${index! + 1} of ${total}: ${member.model.displayName}`
          : `Launching — ${done}.`;
    }
  }
}
