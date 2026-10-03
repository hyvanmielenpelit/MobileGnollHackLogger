import { OnDestroy, inject, Injectable } from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkScoringProfileDto,
  StartBenchmarkRunRequest,
  BenchmarkLastAssessorDto,
  BenchmarkSecondOpinionMode,
  BENCHMARK_SECOND_OPINION_MODES,
  BenchmarkBatteryDto,
  BenchmarkBatteryAttachDto,
  BenchmarkBatteryReusePreviewDto,
  StartBenchmarkBatteryRunRequest
} from '../../../services/admin-benchmark.service';
import { Subscription } from 'rxjs';
import { BenchmarkRunSettings } from '../benchmark.models';
import { refusalText } from '../benchmark-run-format';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';
import { BenchmarkViewSync } from './benchmark-view-sync.service';

/** The Run Benchmark launcher's form, the remembered run settings and the run request it builds. Survives a switch to another sub-tab. */
@Injectable()
export class BenchmarkLauncherState implements OnDestroy {
  private readonly viewSync = inject(BenchmarkViewSync);
  private readonly workspace = inject(BenchmarkWorkspaceStore);
  private benchmarkService = inject(AdminBenchmarkService);
  readonly secondOpinionModeOptions = BENCHMARK_SECOND_OPINION_MODES;

  constructor() {
    // Each list applies the remembered selection it validates as it arrives. Both services live
    // exactly as long as AdminBenchmarkComponent, so the subscriptions need no teardown.
    this.workspace.suitesLoaded$.subscribe(() => this.applyLoadedSuites());
    this.workspace.profilesLoaded$.subscribe(() => this.applyLoadedProfiles());
    this.workspace.batteriesLoaded$.subscribe(loaded => this.applyLoadedBatteries(loaded));
    this.workspace.runLimitsLoaded$.subscribe(() => {
      this.clampRunCountToTarget();
      if (this.reuseEarlierRuns) {
        this.refreshReusePreview();
      }
    });
  }

  /** A remembered suite wins over the first one, but only if it still exists. */
  private applyLoadedSuites(): void {
    const suites = this.workspace.suites;
    const rememberedSuiteId = this.pendingRunSettings?.suiteId ?? null;
    if (rememberedSuiteId != null && suites.some(s => s.id === rememberedSuiteId)) {
      this.selectedSuiteId = rememberedSuiteId;
    } else if (suites.length > 0 && (!this.selectedSuiteId || !suites.some(s => s.id === this.selectedSuiteId))) {
      this.selectedSuiteId = suites[0].id;
    } else if (suites.length === 0) {
      this.selectedSuiteId = null;
    }
    this.markRunSettingsApplied('suite');
    this.loadLastAssessor();
  }

  /** A remembered profile wins over the default one, but only if it still exists. */
  private applyLoadedProfiles(): void {
    const profiles = this.workspace.scoringProfiles;
    const rememberedProfileId = this.pendingRunSettings?.scoringProfileId ?? null;
    const defaultProf = profiles.find(p => p.isDefault);
    if (rememberedProfileId != null && profiles.some(p => p.id === rememberedProfileId)) {
      this.selectedScoringProfileId = rememberedProfileId;
    } else if (defaultProf && !this.selectedScoringProfileId) {
      this.selectedScoringProfileId = defaultProf.id;
    } else if (profiles.length > 0 && !this.selectedScoringProfileId) {
      this.selectedScoringProfileId = profiles[0].id;
    }
    this.markRunSettingsApplied('profile');
  }

  /**
   * Applies the remembered Run Target once the battery list has arrived. A remembered battery that is
   * gone, archived or broken falls back to Single suite, as does a list that failed to load.
   */
  private applyLoadedBatteries(loaded: boolean): void {
    if (!loaded) {
      if (this.pendingRunSettings && !this.runSettingsApplied.battery) {
        this.runTargetKind = 'suite';
      }
      this.markRunSettingsApplied('battery');
      return;
    }
    const pending = this.pendingRunSettings;
    const runnable = this.runnableBatteries;
    if (pending && !this.runSettingsApplied.battery) {
      const remembered = runnable.find(b => b.id === pending.batteryId);
      if (pending.targetKind === 'battery' && remembered) {
        this.runTargetKind = 'battery';
        this.selectedBatteryId = remembered.id;
      } else {
        this.runTargetKind = 'suite';
        this.selectedBatteryId = remembered?.id ?? runnable[0]?.id ?? null;
      }
    } else if (this.selectedBatteryId == null || !runnable.some(b => b.id === this.selectedBatteryId)) {
      this.selectedBatteryId = runnable[0]?.id ?? null;
    }
    this.markRunSettingsApplied('battery');
    this.clampRunCountToTarget();
    this.refreshReusePreview();
  }

  selectedSuiteId: number | null = null;

  selectedScoringProfileId: number | null = null;

  // Run Setup
  testedConfigId: number | null = null;

  assessorConfigId: number | null = null;

  /**
   * Optional third model: re-grades answers the assessor flagged with a critical error or
   * scored below the profile's threshold. Null means no second opinion for this run, which is
   * the default — it spends tokens, and one model checking its own verdict is not a second
   * reading, so there is deliberately no fallback to the assessor.
   */
  secondOpinionConfigId: number | null = null;

  /**
   * Optional panel member B. When set, it grades every answer beside the assessor (member A), the
   * published score is the mean of the two, and the second opinion becomes the reference reader.
   * Null, the default, is a single-assessor run.
   */
  coAssessorConfigId: number | null = null;

  /**
   * Optional model: verifies unverified factual claims against the game source code and wiki
   * using read-only tools. Null means no claim verification for this run, which is the default.
   */
  claimVerifierConfigId: number | null = null;

  /**
   * Optional model: writes the run's Executive Summary and Report for AI Researchers and Developers
   * once, after the run is scored. Null means no AI-written reports, which is the default.
   */
  reportWriterConfigId: number | null = null;

  /**
   * Candidate system prompt response style: false for concise (the production default), true for
   * detailed.
   */
  candidateVerboseMode = false;

  /**
   * Whether the candidate may cite source files and lines: false (the production default, which
   * regular users see only with *Show source code references* on) or true.
   */
  candidateAllowSourceCodeReferences = false;

  /**
   * Per-run override of the scoring profile's second-opinion mode. Null follows the profile, so
   * changing the profile changes the shown default until the operator picks something.
   */
  private secondOpinionModeOverride: number | null = null;

  /**
   * The assessor of the suite's most recent completed run, for the assessor-change advisory.
   * Null until the lookup returns, and carries a null runId for a suite with no completed run.
   */
  lastAssessor: BenchmarkLastAssessorDto | null = null;

  /**
   * How many times to execute the configured request. Bound to a `type="number"` field whose max is
   * `runLimits.maxRunCountPerSeries`, never a literal: raising the configured daily cap must raise
   * the field with it, and the server re-checks against the live guard regardless.
   */
  runCount = 1;

  /**
   * On a cap denial: pause the series in WaitingForCap and retry, rather than stopping it. Either
   * way every completed member is kept and the series stays resumable.
   */
  allowCapWait = false;

  // --- Multi-suite battery runs ---
  //
  // A battery run executes every suite of a battery for one model configuration, one member run at
  // a time, Runs per Suite rounds over the suites. The launcher's Run Target chooses it; its banner,
  // poller and Web Lock mirror the series ones, and it signals completion once for all its members.

  /** What Start launches: one suite (a run or a series) or a battery. */
  runTargetKind: 'suite' | 'battery' = 'suite';

  selectedBatteryId: number | null = null;

  /** Reuse earlier runs at a battery start. A per-start decision, so never stored with the run settings. */
  reuseEarlierRuns = false;

  /** Which slots earlier runs would fill for the launcher's current battery request; null while none is known. */
  reusePreview: BenchmarkBatteryReusePreviewDto | null = null;

  reusePreviewLoading = false;

  reusePreviewError: string | null = null;

  /** The preview request in flight; a newer one unsubscribes it, which cancels the HTTP call. */
  reusePreviewSubscription: Subscription | null = null;

  /** Whether a run or series completion plays the chime. Bound to the Run tab's own checkbox. */
  completionSound = true;

  /**
   * Whether a run or series completion also raises a desktop notification. Independent of the
   * sound: either, both or neither may be on. Bound to the Run tab's second checkbox.
   */
  completionNotification = false;

  /** A co-assessor is selected, so the run being set up is a two-member panel. */
  get isPanelLaunch(): boolean {
    return this.coAssessorConfigId != null;
  }

  /** "Family" is the provider, compared the way the server's IsSameProvider compares it. */
  static sameProvider(a: string | null | undefined, b: string | null | undefined): boolean {
    return !!a && !!b && a.trim().toLowerCase() === b.trim().toLowerCase();
  }

  /**
   * The mode that will apply to this run: the operator's override, else the selected profile's
   * default. Inert without a second-opinion assessor — which is the hard gate that silently
   * produced the 2026-09-03 run's zero second verdicts, so the control says so rather than
   * looking configured.
   */
  get secondOpinionMode(): number {
    // A panel run's reference reader grades every answer, blind; the server forces it.
    if (this.isPanelLaunch) {
      return BenchmarkSecondOpinionMode.All;
    }
    return this.secondOpinionModeOverride
      ?? this.selectedScoringProfile?.secondOpinionMode
      ?? BenchmarkSecondOpinionMode.Flagged;
  }

  set secondOpinionMode(value: number) {
    this.secondOpinionModeOverride = Number(value);
  }

  loadLastAssessor(): void {
    const suiteId = this.selectedSuiteId;
    if (suiteId == null) {
      this.lastAssessor = null;
      return;
    }

    this.benchmarkService.getLastAssessor(suiteId).subscribe({
      next: (dto) => {
        this.lastAssessor = dto;
        this.viewSync.notify();
      },
      // An advisory that cannot be computed is simply not shown: the run must not be blocked
      // because a comparison lookup failed.
      error: () => {
        this.lastAssessor = null;
      }
    });
  }

  setDefaultModelSelections() {
    const benchmarkModels = this.workspace.benchmarkCapableConfigs;
    if (benchmarkModels.length > 0) {
      // A remembered configuration wins over the first one, but only while it still qualifies:
      // benchmarkCapableConfigs filters on the Benchmark role bit, hasApiKey and isEnabled, so one that
      // was disabled or lost its key falls back rather than leaving a selection the server would reject.
      const remembered = this.pendingRunSettings;
      const qualifies = (id: number | null | undefined): boolean =>
        id != null && benchmarkModels.some(m => m.id === id);

      if (qualifies(remembered?.testedConfigId)) {
        this.testedConfigId = remembered!.testedConfigId;
      } else if (!this.testedConfigId || !benchmarkModels.some(m => m.id === this.testedConfigId)) {
        this.testedConfigId = benchmarkModels[0].id;
      }

      if (qualifies(remembered?.assessorConfigId)) {
        this.assessorConfigId = remembered!.assessorConfigId;
      } else if (!this.assessorConfigId || !benchmarkModels.some(m => m.id === this.assessorConfigId)) {
        this.assessorConfigId = benchmarkModels[0].id;
      }

      // The optional roles restore to null when their configuration no longer qualifies, which is the
      // same as "not selected" and is what the run request already means by a null id.
      if (remembered) {
        if (remembered.coAssessorConfigId != null) {
          this.coAssessorConfigId = qualifies(remembered.coAssessorConfigId)
            ? remembered.coAssessorConfigId
            : null;
        }
        if (remembered.secondOpinionConfigId != null) {
          this.secondOpinionConfigId = qualifies(remembered.secondOpinionConfigId)
            ? remembered.secondOpinionConfigId
            : null;
        }
        if (remembered.claimVerifierConfigId != null) {
          this.claimVerifierConfigId = qualifies(remembered.claimVerifierConfigId)
            ? remembered.claimVerifierConfigId
            : null;
        }
        if (remembered.reportWriterConfigId != null) {
          this.reportWriterConfigId = qualifies(remembered.reportWriterConfigId)
            ? remembered.reportWriterConfigId
            : null;
        }
      }
    } else {
      this.testedConfigId = null;
      this.assessorConfigId = null;
    }

    // Only counts as applied when there was actually a list to validate against: called from ngOnInit
    // before the systemConfigs input has arrived, this method has done nothing.
    if (benchmarkModels.length > 0) {
      this.markRunSettingsApplied('configs');
    }
  }

  // --- Run setting recall ---
  //
  // Follows AdminComponent.persistConfigFilter / restoreConfigFilter: a private static key, try/catch
  // around every localStorage access because it throws in private-browsing modes, and a whitelisting
  // restore that drops anything unrecognised.
  //
  // Every restored id is validated against the list it must come from — benchmarkCapableConfigs filters on
  // the Benchmark role bit, hasApiKey and isEnabled — so a configuration that was disabled, lost its key or
  // lost its role falls back to the existing default rather than leaving a dangling selection that fails
  // server-side at run time.

  private static readonly RUN_SETTINGS_STORAGE_KEY = 'overseer_admin_benchmark_run_settings';

  /**
   * The stored settings, read once in ngOnInit and applied by whichever loader owns each field, because
   * the restore cannot run before the data it validates against exists: suites arrive from loadSuites,
   * profiles from loadProfiles, and configurations from the systemConfigs input via ngOnChanges.
   *
   * Cleared once applied, so a later ngOnChanges cannot resurrect a stale selection over one the operator
   * has since made by hand.
   */
  pendingRunSettings: BenchmarkRunSettings | null = null;

  /**
   * Saved in startBenchmark before the request is sent: the operator's choices are worth remembering
   * whether or not the server accepts the run.
   *
   * The same-provider acknowledgments (acknowledgeSameProvider for the assessor and
   * acknowledgeSameProviderReportWriter for the report writer) are deliberately not persisted. They are
   * per-run safety acknowledgments, and silently remembering them would defeat the warning dialog they
   * exist to gate. Neither are the
   * difficulty-assessor, retry-assessor, generation-model or calibration-assessor selections, which are
   * not part of setting up a run.
   */
  persistRunSettings(): void {
    try {
      const settings: BenchmarkRunSettings = {
        suiteId: this.selectedSuiteId,
        testedConfigId: this.testedConfigId,
        assessorConfigId: this.assessorConfigId,
        coAssessorConfigId: this.coAssessorConfigId,
        secondOpinionConfigId: this.secondOpinionConfigId,
        claimVerifierConfigId: this.claimVerifierConfigId,
        reportWriterConfigId: this.reportWriterConfigId,
        // The override, not the getter: a run left on the profile default must keep following the
        // profile, and persisting the resolved value would freeze it at whatever the profile said today.
        secondOpinionMode: this.secondOpinionModeOverride,
        scoringProfileId: this.selectedScoringProfileId,
        verboseMode: this.candidateVerboseMode,
        allowSourceCodeReferences: this.candidateAllowSourceCodeReferences,
        runCount: this.effectiveRunCount,
        targetKind: this.runTargetKind,
        batteryId: this.selectedBatteryId,
        completionSound: this.completionSound,
        completionNotification: this.completionNotification
      };
      localStorage.setItem(
        BenchmarkLauncherState.RUN_SETTINGS_STORAGE_KEY, JSON.stringify(settings));
    } catch {
      // Storage throws in private-browsing modes. Failing to remember a selection is not worth
      // surfacing to the operator.
    }
  }

  /** Reads the stored blob into pendingRunSettings, and restores the fields no loader owns. */
  restoreRunSettings(): void {
    let parsed: unknown;
    try {
      const stored = localStorage.getItem(BenchmarkLauncherState.RUN_SETTINGS_STORAGE_KEY);
      if (!stored) { return; }
      parsed = JSON.parse(stored);
    } catch {
      return;                                   // every default stands
    }

    const raw = parsed as Partial<BenchmarkRunSettings> | null;
    if (!raw || typeof raw !== 'object') { return; }

    const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v)) ? v : null;

    this.pendingRunSettings = {
      suiteId: num(raw.suiteId),
      testedConfigId: num(raw.testedConfigId),
      assessorConfigId: num(raw.assessorConfigId),
      coAssessorConfigId: num(raw.coAssessorConfigId),
      secondOpinionConfigId: num(raw.secondOpinionConfigId),
      claimVerifierConfigId: num(raw.claimVerifierConfigId),
      reportWriterConfigId: num(raw.reportWriterConfigId),
      secondOpinionMode: num(raw.secondOpinionMode),
      scoringProfileId: num(raw.scoringProfileId),
      verboseMode: typeof raw.verboseMode === 'boolean' ? raw.verboseMode : null,
      allowSourceCodeReferences: typeof raw.allowSourceCodeReferences === 'boolean' ? raw.allowSourceCodeReferences : null,
      runCount: num(raw.runCount),
      targetKind: raw.targetKind === 'battery' || raw.targetKind === 'suite' ? raw.targetKind : null,
      batteryId: num(raw.batteryId),
      completionSound: typeof raw.completionSound === 'boolean' ? raw.completionSound : null,
      completionNotification: typeof raw.completionNotification === 'boolean' ? raw.completionNotification : null
    };

    // These need no list to validate against, so they restore immediately.
    if (this.pendingRunSettings.verboseMode !== null) {
      this.candidateVerboseMode = this.pendingRunSettings.verboseMode;
    }
    // Absent (a blob predating this field) leaves the Disallowed default standing.
    if (this.pendingRunSettings.allowSourceCodeReferences !== null) {
      this.candidateAllowSourceCodeReferences = this.pendingRunSettings.allowSourceCodeReferences;
    }
    // Absent (a blob predating this field, or storage that threw) leaves the true default standing.
    if (this.pendingRunSettings.completionSound !== null) {
      this.completionSound = this.pendingRunSettings.completionSound;
    }
    // Restoring the choice does not re-request permission; notify() itself is a no-op once the
    // browser's own permission state is no longer granted, so restoring optimistically is safe.
    if (this.pendingRunSettings.completionNotification !== null) {
      this.completionNotification = this.pendingRunSettings.completionNotification;
    }
    const count = this.pendingRunSettings.runCount;
    if (count !== null && count >= 1) {
      const intCount = Math.floor(count);
      const max = this.runCountTargetPending ? null : this.runCountMax;
      this.runCount = (max != null && intCount > max) ? max : intCount;
    }
    const mode = this.pendingRunSettings.secondOpinionMode;
    if (mode !== null && this.secondOpinionModeOptions.some(o => o.value === mode)) {
      this.secondOpinionModeOverride = mode;
    }
  }

  /**
   * Which of the four list-backed fields have been applied. The loaders complete in whatever order their
   * requests return, and setDefaultModelSelections runs from ngOnInit before either has answered, so the
   * stored blob can only be dropped once all four have had their turn — dropping it as soon as any one of
   * them finishes would leave the others falling back to their defaults. The battery part is the Run
   * Target and its battery, applied by loadBatteries.
   */
  runSettingsApplied = { suite: false, profile: false, configs: false, battery: false };

  /** Marks one part applied, and drops the stored blob once all four are. */
  markRunSettingsApplied(part: 'suite' | 'profile' | 'configs' | 'battery'): void {
    if (!this.pendingRunSettings) return;
    this.runSettingsApplied[part] = true;
    const done = this.runSettingsApplied;
    if (done.suite && done.profile && done.configs && done.battery) {
      // Cleared so a later ngOnChanges cannot resurrect a stale selection over one the operator has
      // since made by hand.
      this.pendingRunSettings = null;
    }
  }

  // --- Run Execution ---

  /**
   * The same-provider acknowledgments given in the current start attempt, by role. A new attempt
   * starts with none; the confirmation adds its role and the request is sent again with every one
   * given so far, so an assessor warning and then a report-writer warning resolve both. Never stored.
   */
  launchAcknowledgments = { assessor: false, reportWriter: false };

  /** The launcher's run request: what a run or a series starts, and every battery member's template. */
  buildRunRequest(suiteId: number, testedConfigId: number, assessorConfigId: number): StartBenchmarkRunRequest {
    return {
      suiteId,
      testedModelConfigurationId: testedConfigId,
      assessorModelConfigurationId: assessorConfigId,
      secondOpinionAssessorModelConfigurationId: this.secondOpinionConfigId,
      // Sent only when an assessor is selected: without one the mode is inert, and sending Off
      // would be indistinguishable from "the operator chose Never".
      secondOpinionMode: this.secondOpinionConfigId != null ? this.secondOpinionMode : null,
      claimVerifierModelConfigurationId: this.claimVerifierConfigId,
      reportWriterModelConfigurationId: this.reportWriterConfigId,
      verboseMode: this.candidateVerboseMode,
      allowSourceCodeReferences: this.candidateAllowSourceCodeReferences,
      scoringProfileId: this.selectedScoringProfileId,
      acknowledgeSameProvider: this.launchAcknowledgments.assessor,
      // Only once acknowledged, so every other request carries the body it always has.
      ...(this.launchAcknowledgments.reportWriter ? { acknowledgeSameProviderReportWriter: true } : {}),
      // Only on a panel run, so a single-assessor request carries the body it always has.
      ...(this.coAssessorConfigId != null ? { coAssessorModelConfigurationId: this.coAssessorConfigId } : {})
    };
  }

  // --- Multi-run series execution ---

  /**
   * The run count actually in force. A non-numeric or out-of-range field value resolves to 1 rather
   * than to an error, because the field is a courtesy and the server is the authority: the worst a
   * bad value here may do is start one run, never N of them.
   */
  get effectiveRunCount(): number {
    const n = Math.floor(Number(this.runCount));
    if (!Number.isFinite(n) || n < 1) return 1;
    const max = this.runCountMax;
    return max != null && n > max ? max : n;
  }

  /** The run count field's `max` for the current Run Target: runs per series, or runs per suite. */
  get runCountMax(): number | null {
    return this.isBatteryTarget ? this.maxRunsPerSuite : this.maxRunCountPerSeries;
  }

  /**
   * The Number of runs field's `max`, from GET runs/limits. Null until the caps arrive, which
   * leaves the field unbounded on the client and bounded on the server — the safe direction, since
   * an unknown cap must not silently become 1.
   */
  get maxRunCountPerSeries(): number | null {
    return this.workspace.runLimits?.maxRunCountPerSeries ?? null;
  }

  // --- Run Target: battery ---

  get isBatteryTarget(): boolean {
    return this.runTargetKind === 'battery';
  }

  /** The batteries the launcher offers: not archived, no deleted suite and no validation error. */
  get runnableBatteries(): BenchmarkBatteryDto[] {
    return this.workspace.launcherBatteries.filter(b => BenchmarkLauncherState.isRunnableBattery(b));
  }

  private static isRunnableBattery(battery: BenchmarkBatteryDto): boolean {
    return !battery.isArchived
      && (battery.brokenSuiteNames?.length ?? 0) === 0
      && (battery.validationErrors?.length ?? 0) === 0;
  }

  get selectedBattery(): BenchmarkBatteryDto | undefined {
    return this.selectedBatteryId == null
      ? undefined
      : this.runnableBatteries.find(b => b.id === this.selectedBatteryId);
  }

  /** The suite id the start request carries: the selected suite, or a battery's first suite, which the server replaces per member. */
  get launchSuiteId(): number | null {
    if (!this.isBatteryTarget) return this.selectedSuiteId;
    return this.selectedBattery?.suites.find(s => s.suiteId != null)?.suiteId ?? null;
  }

  /** The battery start the launcher would send now, without its attach list; null while a field it needs is unset. */
  buildBatteryStartRequest(): StartBenchmarkBatteryRunRequest | null {
    const batteryId = this.selectedBattery?.id;
    const suiteId = this.launchSuiteId;
    if (batteryId == null || suiteId == null || this.testedConfigId == null || this.assessorConfigId == null) {
      return null;
    }
    return {
      batteryId,
      runsPerSuite: this.effectiveRunCount,
      allowCapWait: this.allowCapWait,
      run: this.buildRunRequest(suiteId, this.testedConfigId, this.assessorConfigId)
    };
  }

  /**
   * Asks the server which earlier runs a start of the current battery request would reuse, or
   * clears the preview when reuse is off or the request is incomplete. A newer request cancels the
   * one in flight, so the projection never shows the preview of settings already changed.
   */
  refreshReusePreview(): void {
    this.reusePreviewSubscription?.unsubscribe();
    this.reusePreviewSubscription = null;
    this.reusePreview = null;
    this.reusePreviewError = null;

    const req = this.isBatteryTarget && this.reuseEarlierRuns ? this.buildBatteryStartRequest() : null;
    if (!req) {
      this.reusePreviewLoading = false;
      return;
    }

    this.reusePreviewLoading = true;
    this.reusePreviewSubscription = this.benchmarkService.previewBatteryReuse(req).subscribe({
      next: (preview) => {
        this.reusePreviewLoading = false;
        this.reusePreview = preview;
        this.viewSync.notify();
      },
      error: (err) => {
        this.reusePreviewLoading = false;
        this.reusePreviewError = refusalText(err, 'The server could not preview the reuse.');
        this.viewSync.notify();
      }
    });
  }

  /** The preview Start would act on: reuse is on and it was computed for the battery and round count shown. */
  get activeReusePreview(): BenchmarkBatteryReusePreviewDto | null {
    const preview = this.reusePreview;
    if (!this.isBatteryTarget || !this.reuseEarlierRuns || !preview) return null;
    return preview.batteryId === this.selectedBattery?.id && preview.runsPerSuite === this.effectiveRunCount
      ? preview
      : null;
  }

  /** The runs Start sends as `attach`: the active preview's choice, or none. */
  get reuseAttachList(): BenchmarkBatteryAttachDto[] {
    return (this.activeReusePreview?.attach ?? []).map(a => ({ ...a }));
  }

  /**
   * Runs per Suite's ceiling: one battery run may plan at most `maxMembersPerBattery` launches, so
   * K suites allow floor(max / K) rounds. Null while the limit or the battery is unknown.
   */
  get maxRunsPerSuite(): number | null {
    const max = this.workspace.runLimits?.maxMembersPerBattery;
    const suiteCount = this.selectedBattery?.suites.length ?? 0;
    if (max == null || suiteCount === 0) return null;
    return Math.max(1, Math.floor(max / suiteCount));
  }

  /**
   * True while a remembered Battery target waits for the battery list: the run count then belongs
   * to Runs per Suite, so the single-suite ceiling must not lower it yet.
   */
  private get runCountTargetPending(): boolean {
    return this.pendingRunSettings?.targetKind === 'battery' && !this.runSettingsApplied.battery;
  }

  /** Lowers the shared run count to the current target's ceiling. */
  clampRunCountToTarget(): void {
    if (this.runCountTargetPending) return;
    const max = this.runCountMax;
    const n = Math.floor(Number(this.runCount));
    if (max != null && Number.isFinite(n) && n > max) {
      this.runCount = max;
    }
  }

  get selectedSuite(): BenchmarkSuiteDto | undefined {
    return this.workspace.suites.find(s => s.id === this.selectedSuiteId);
  }

  get selectedScoringProfile(): BenchmarkScoringProfileDto | undefined {
    return this.workspace.scoringProfiles.find(p => p.id === this.selectedScoringProfileId);
  }

  ngOnDestroy(): void {
    this.reusePreviewSubscription?.unsubscribe();
    this.reusePreviewSubscription = null;
  }
}
