import { Component, ChangeDetectorRef, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkRunSummaryDto,
  BenchmarkScoringProfileDto,
  SameProviderWarningDto,
  BENCHMARK_SECOND_OPINION_MODES,
  BenchmarkBatteryReusePreviewSlotDto,
  BoardFactsCheckDto,
  BoardFactIssueDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { BenchmarkCompletionSoundService } from '../../../services/benchmark-completion-sound.service';
import {
  ModelPickerComponent
} from '../../../shared/model-picker/model-picker.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import { reportWriterRefusal, reportWriterWarning } from '../run-ai-reports/report-writer-policy';
import {
  formatCostAmount,
  formatStatus,
  formatElapsed,
  DELIBERATING_THINKING_LEVELS,
  INTERACTIVE_SPEED_TARGET_MAX_MS,
  MISSING_BOARD_QUOTE_LIST_CAP
} from '../benchmark-run-format';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { BenchmarkLauncherState } from '../state/benchmark-launcher.state';
import { BenchmarkDifficultyJobService } from '../state/benchmark-difficulty-job.service';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** The Run Benchmark sub-tab: the launcher, the active run, series and battery banners, and the start dialogs. */
@Component({
  selector: 'app-benchmark-run-tab',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, FormsModule, ModelPickerComponent, InfoTipComponent
  ],
  templateUrl: './benchmark-run-tab.component.html',
  styleUrls: ['./benchmark-run-tab.component.scss']
})
export class BenchmarkRunTabComponent {
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

  /** Mirrors the server's refusal: the two panel members must come from different providers. */
  get showCoAssessorSameProviderAdvisory(): boolean {
    return this.launcher.isPanelLaunch &&
      BenchmarkLauncherState.sameProvider(this.selectedAssessorModel?.provider, this.selectedCoAssessorModel?.provider);
  }

  /** Mirrors the server's refusal: neither panel member may be the model under test. */
  get showCoAssessorCandidateAdvisory(): boolean {
    const candidate = this.selectedTestedModel;
    return this.launcher.isPanelLaunch &&
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
    return reportWriterRefusal(this.selectedReportWriterModel, this.selectedTestedModel);
  }

  /**
   * A writer from the model under test's provider: advisory, Start stays available and the server
   * asks for the acknowledgment. Empty when there is no writer, it is refused, or its provider differs.
   */
  get reportWriterLaunchWarning(): string {
    return reportWriterWarning(this.selectedReportWriterModel, this.selectedTestedModel);
  }

  /**
   * The roles a panel run's reference reader or claim verifier shares a provider with. Advisory:
   * a non-scoring role is most useful from a family that is neither a candidate nor a panel member.
   */
  private sharedFamilyRoles(provider: string | null | undefined): string[] {
    if (!this.launcher.isPanelLaunch || !provider) return [];
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
    return this.launcher.claimVerifierConfigId != null &&
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

  onSelectedSuiteChanged(): void {
    this.launcher.loadLastAssessor();
  }

  selectTestedModel(config: SystemAiConfigDto | null) {
    if (!config) return;
    this.launcher.testedConfigId = config.id;
    this.launcher.refreshReusePreview();
  }

  selectAssessorModel(config: SystemAiConfigDto | null) {
    if (!config) return;
    this.launcher.assessorConfigId = config.id;
    this.launcher.refreshReusePreview();
  }

  selectSecondOpinionModel(config: SystemAiConfigDto | null) {
    this.launcher.secondOpinionConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
  }

  selectCoAssessorModel(config: SystemAiConfigDto | null) {
    this.launcher.coAssessorConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
  }

  selectClaimVerifierModel(config: SystemAiConfigDto | null) {
    this.launcher.claimVerifierConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
  }

  selectReportWriterModel(config: SystemAiConfigDto | null) {
    this.launcher.reportWriterConfigId = config?.id ?? null;
    this.launcher.refreshReusePreview();
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
   * the series projection. A battery has a projection of its own.
   */
  get isMultiRunRequested(): boolean {
    return !this.launcher.isBatteryTarget && this.launcher.effectiveRunCount > 1;
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
    this.cdr.detectChanges();
  }

  onSelectedBatteryChanged(): void {
    this.launcher.clampRunCountToTarget();
    this.launcher.refreshReusePreview();
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
    return this.monitor.activeSeries != null && !this.monitor.multiRunDialogVisible && !this.monitor.seriesIsFinished;
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
          + `${series.completedRunCount} of ${series.requestedRunCount} runs completed.`;
      case 'Pending':
        return `Launching run 1 of ${series.requestedRunCount}.`;
      case 'Running':
        return `Run ${current} of ${series.requestedRunCount}.`;
      default:
        return `${series.status} — ${series.completedRunCount} of ${series.requestedRunCount} runs completed.`;
    }
  }

  /** The Continue button's label, which names the stop reason rather than hiding it behind a verb. */
  get seriesContinueLabel(): string {
    const reason = this.monitor.activeSeries?.stopReasonText || this.monitor.activeSeries?.stopReason;
    return reason ? `Continue (${reason})` : 'Continue';
  }

  /** True once a refused resume has named a moved hash, which is what offers the override. */
  get seriesInstrumentChanged(): boolean {
    return (this.monitor.activeSeries?.changedInstrumentHashes?.length ?? 0) > 0;
  }

  /** Terminal with nothing left to offer; Stopped and Completed with errors may still be continued. */
  get batteryIsFinished(): boolean {
    const status = this.monitor.activeBatteryRun?.status;
    return status === 'Completed' || status === 'Cancelled' || status === 'Failed';
  }

  get batteryIsStopped(): boolean {
    return this.monitor.activeBatteryRun?.status === 'Stopped';
  }

  get batteryIsWaitingForCap(): boolean {
    return this.monitor.activeBatteryRun?.status === 'WaitingForCap';
  }

  /** Stopped because a member is not comparable with the others: only a re-run or a cancel is offered. */
  get batteryStoppedOnInstrumentChange(): boolean {
    return this.batteryIsStopped && this.monitor.activeBatteryRun?.stopReason === 'InstrumentChanged';
  }

  /** Continue is offered for a resumable battery run that did not stop on an instrument change. */
  get batteryCanContinue(): boolean {
    return !!this.monitor.activeBatteryRun?.resumable && !this.monitor.batteryIsLive && !this.batteryStoppedOnInstrumentChange;
  }

  /** The battery banner shows while the battery run is live, stopped or continuable, and its dialog is closed. */
  get batteryBannerVisible(): boolean {
    return this.monitor.activeBatteryRun != null && !this.monitor.batteryDialogVisible && !this.batteryIsFinished;
  }

  /** *Suite s of K · round r of R*, or the state that replaces it. */
  get batteryProgressLabel(): string {
    const batteryRun = this.monitor.activeBatteryRun;
    if (!batteryRun) return '';
    const suites = `${batteryRun.completedSuiteCount} of ${batteryRun.suiteCount} suites completed`;
    switch (batteryRun.status) {
      case 'WaitingForCap':
        return `Waiting for run cap — ${suites}.`;
      case 'Stopped':
        return `Stopped — ${batteryRun.stopReasonText || batteryRun.stopReason || 'reason not recorded'}. ${suites}.`;
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

  /** The Continue button's label, naming the stop reason. */
  get batteryContinueLabel(): string {
    const reason = this.monitor.activeBatteryRun?.stopReasonText || this.monitor.activeBatteryRun?.stopReason;
    return reason ? `Continue (${reason})` : 'Continue';
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

  /**
   * The *Test sound* button: plays under this click's user gesture, which also unlocks later
   * programmatic playback on browsers that require one interaction before audio is allowed.
   */
  testCompletionSound(): void {
    this.monitor.armCompletionSignalsFromGesture();
    this.monitor.completionSoundStatus = null;
    this.completionSoundService.prime().then(outcome => {
      this.monitor.lastCompletionSoundOutcome = outcome;
      if (outcome === 'blocked') {
        this.monitor.completionSoundStatus = 'Playback was blocked by the browser — press Test sound once to allow it.';
      }
      this.cdr.detectChanges();
    });
  }

  get showProfileFitAdvisory(): boolean {
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
      !this.panelLaunchRefusal &&
      !this.reportWriterLaunchRefusal;
  }

  /** Names the first condition Start Benchmark is waiting on, for the button's aria-disabled hint. Empty once canStartRun is true. */
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
    if (this.panelLaunchRefusal) {
      return this.panelLaunchRefusal;
    }
    if (this.reportWriterLaunchRefusal) {
      return this.reportWriterLaunchRefusal;
    }
    return '';
  }
}
