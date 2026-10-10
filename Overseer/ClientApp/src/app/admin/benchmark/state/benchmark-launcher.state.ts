import { OnDestroy, inject, Injectable } from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkScoringProfileDto,
  BenchmarkRunDetailDto,
  StartBenchmarkRunRequest,
  BenchmarkLastAssessorDto,
  BenchmarkSecondOpinionMode,
  BENCHMARK_SECOND_OPINION_MODES,
  BenchmarkBatteryDto,
  BenchmarkBatteryAttachDto,
  BenchmarkBatteryReusePreviewDto,
  StartBenchmarkBatteryRunRequest,
  BenchmarkModelBatchFindingDto,
  BenchmarkModelBatchPreflightResponse,
  BenchmarkModelBatchProjectionDto,
  StartBenchmarkModelBatchRequest
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { Observable, Subject, Subscription, catchError, debounceTime, map, of, switchMap } from 'rxjs';
import { BenchmarkLauncherRunMode, BenchmarkModelBatchOrderChoice, BenchmarkRunSettings } from '../benchmark.models';
import { parsePromptOptions, refusalText } from '../benchmark-run-format';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';
import { BenchmarkViewSync } from './benchmark-view-sync.service';
import type { ModelBatchPrefill } from './benchmark-shell-bridge.service';

/** A run whose setup the launcher took over, and each recorded setting it could not take over. */
export interface BenchmarkRunPrefillResult {
  runId: number;
  notes: string[];
}

/** A model role the launcher fills from a system configuration. */
export type BenchmarkLauncherRole = 'tested' | 'assessor' | 'coAssessor' | 'secondOpinion' | 'claimVerifier' | 'reportWriter';

/** The launcher's roles in the order its fields show them. */
export const BENCHMARK_LAUNCHER_ROLES: readonly BenchmarkLauncherRole[] =
  ['tested', 'assessor', 'coAssessor', 'secondOpinion', 'claimVerifier', 'reportWriter'];

/** A selected role whose configuration uses a model removed from the model catalog. */
export interface BenchmarkRetiredRole {
  role: BenchmarkLauncherRole;
  /** The role as its field is labeled: *Model Under Test*, *Assessor*, … */
  label: string;
  config: SystemAiConfigDto;
}

/** How long the launcher waits for the batch settings to settle before it asks the server to check them. */
export const MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS = 300;

/** The first retry delay after a failed batch check; each further failure doubles it, up to the cap. */
const MODEL_BATCH_PREFLIGHT_RETRY_MS = 2000;
const MODEL_BATCH_PREFLIGHT_RETRY_MAX_MS = 30000;

/** What one batch check came back with. */
type ModelBatchPreflightOutcome =
  | { kind: 'none' }
  | { kind: 'ok'; response: BenchmarkModelBatchPreflightResponse }
  | { kind: 'error'; message: string };

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
      this.requestPreflight();
    });
    // One check in flight: a newer settled change cancels the one before it.
    this.preflightSubscription = this.preflightRequested$.pipe(
      debounceTime(MODEL_BATCH_PREFLIGHT_DEBOUNCE_MS),
      switchMap(() => this.runPreflight())
    ).subscribe(outcome => this.applyPreflightOutcome(outcome));
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
    this.listsArrived.suite = true;
    this.markRunSettingsApplied('suite');
    this.loadLastAssessor();
    this.applyPendingPrefill();
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
    this.listsArrived.profile = true;
    this.markRunSettingsApplied('profile');
    this.applyPendingPrefill();
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
      this.applyPendingPrefill();
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
    this.applyPendingPrefill();
    this.requestPreflight();
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
   * way every completed member is kept and the series stays resumable. A per-start decision, so never
   * stored with the run settings.
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
      // A configuration whose model left the catalog still qualifies and stays selected, so the
      // launcher's refusal under its field explains it.
      const remembered = this.pendingRunSettings;
      const qualifies = (id: number | null | undefined): boolean =>
        id != null && benchmarkModels.some(m => m.id === id);
      const fellBack: BenchmarkLauncherRole[] = [];

      if (qualifies(remembered?.testedConfigId)) {
        this.testedConfigId = remembered!.testedConfigId;
      } else {
        if (remembered?.testedConfigId != null) fellBack.push('tested');
        if (!this.testedConfigId || !benchmarkModels.some(m => m.id === this.testedConfigId)) {
          this.testedConfigId = benchmarkModels[0].id;
        }
      }

      if (qualifies(remembered?.assessorConfigId)) {
        this.assessorConfigId = remembered!.assessorConfigId;
      } else {
        if (remembered?.assessorConfigId != null) fellBack.push('assessor');
        if (!this.assessorConfigId || !benchmarkModels.some(m => m.id === this.assessorConfigId)) {
          this.assessorConfigId = benchmarkModels[0].id;
        }
      }

      // The optional roles restore to null when their configuration no longer qualifies, which is the
      // same as "not selected" and is what the run request already means by a null id.
      if (remembered) {
        const restoreOptional = (role: BenchmarkLauncherRole, id: number | null, assign: (value: number | null) => void): void => {
          if (id == null) return;
          if (qualifies(id)) {
            assign(id);
          } else {
            assign(null);
            fellBack.push(role);
          }
        };
        restoreOptional('coAssessor', remembered.coAssessorConfigId, v => { this.coAssessorConfigId = v; });
        restoreOptional('secondOpinion', remembered.secondOpinionConfigId, v => { this.secondOpinionConfigId = v; });
        restoreOptional('claimVerifier', remembered.claimVerifierConfigId, v => { this.claimVerifierConfigId = v; });
        restoreOptional('reportWriter', remembered.reportWriterConfigId, v => { this.reportWriterConfigId = v; });
        this.restoreNotes = fellBack.map(role => this.restoreNote(role));
        // A batch model that no longer qualifies is dropped; the rest keep their choice and order.
        if (remembered.batchModelIds != null) {
          this.batchModelKeys = BenchmarkLauncherState.distinct(remembered.batchModelIds.filter(qualifies));
          this.batchOrderKeys = BenchmarkLauncherState.mergeOrder(
            (remembered.batchOrderIds ?? []).filter(qualifies), this.batchModelKeys);
        }
      }
    } else {
      this.testedConfigId = null;
      this.assessorConfigId = null;
    }

    // Only counts as applied when there was actually a list to validate against: called from ngOnInit
    // before the systemConfigs input has arrived, this method has done nothing.
    if (benchmarkModels.length > 0) {
      this.listsArrived.configs = true;
      this.markRunSettingsApplied('configs');
      this.applyPendingPrefill();
      this.requestPreflight();
    }
  }

  /**
   * One line per role whose remembered configuration was gone, disabled or no longer had the
   * Benchmark role when the configurations arrived, naming what was chosen instead. Shown once in
   * the New Benchmark Run card and never stored.
   */
  restoreNotes: string[] = [];

  /** Hides the restore notes. */
  clearRestoreNotes(): void {
    this.restoreNotes = [];
  }

  private restoreNote(role: BenchmarkLauncherRole): string {
    const config = this.selectedConfigFor(role);
    const chosen = config ? (config.displayName || config.modelId || `configuration #${config.id}`) : 'none';
    return `${this.roleLabel(role)}: the remembered configuration is no longer available, so ${chosen} was chosen.`;
  }

  /** The role as its launcher field is labeled. */
  roleLabel(role: BenchmarkLauncherRole): string {
    switch (role) {
      case 'tested': return 'Model Under Test';
      case 'assessor': return 'Assessor';
      case 'coAssessor': return 'Co-Assessor';
      case 'secondOpinion': return this.isPanelLaunch ? 'Reference Reader' : 'Second Reader';
      case 'claimVerifier': return 'Claim Verifier';
      case 'reportWriter': return 'Report Writer';
    }
  }

  /** The configuration id selected for the role, or null. */
  selectedConfigIdFor(role: BenchmarkLauncherRole): number | null {
    switch (role) {
      case 'tested': return this.testedConfigId;
      case 'assessor': return this.assessorConfigId;
      case 'coAssessor': return this.coAssessorConfigId;
      case 'secondOpinion': return this.secondOpinionConfigId;
      case 'claimVerifier': return this.claimVerifierConfigId;
      case 'reportWriter': return this.reportWriterConfigId;
    }
  }

  /** The benchmark-capable configuration selected for the role, or undefined. */
  selectedConfigFor(role: BenchmarkLauncherRole): SystemAiConfigDto | undefined {
    const id = this.selectedConfigIdFor(role);
    return id == null ? undefined : this.workspace.benchmarkCapableConfigs.find(c => c.id === id);
  }

  /**
   * The first selected role, in field order, whose configuration uses a model removed from the model
   * catalog; null when there is none. A model batch's Model Under Test field is hidden, so batch mode
   * checks the grading roles only. The server refuses such a run regardless.
   */
  retiredRoleRefusal(): BenchmarkRetiredRole | null {
    for (const role of BENCHMARK_LAUNCHER_ROLES) {
      if (role === 'tested' && this.isModelBatch) continue;
      const config = this.selectedConfigFor(role);
      if (config?.modelAvailability?.status === 'retired') {
        return { role, label: this.roleLabel(role), config };
      }
    }
    return null;
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
   * Saved on every operator change of a launcher field, and again at Start before the request is sent:
   * the operator's choices are worth remembering whether or not the server accepts the run. A loader's
   * own fallback does not save.
   *
   * The same-provider acknowledgments (acknowledgeSameProvider for the assessor and
   * acknowledgeSameProviderReportWriter for the report writer) are deliberately not persisted. They are
   * per-run safety acknowledgments, and silently remembering them would defeat the warning dialog they
   * exist to gate. Neither are the
   * difficulty-assessor, retry-assessor, generation-model or calibration-assessor selections, which are
   * not part of setting up a run. Nor are *Wait when the run cap blocks the next run* (allowCapWait) and
   * *Reuse earlier runs* (reuseEarlierRuns), which are decided at each start, nor a model batch's
   * acknowledged warnings (acknowledgedFindingKeys).
   */
  persistRunSettings(): void {
    try {
      localStorage.setItem(
        BenchmarkLauncherState.RUN_SETTINGS_STORAGE_KEY, JSON.stringify(this.runSettingsSnapshot()));
    } catch {
      // Storage throws in private-browsing modes. Failing to remember a selection is not worth
      // surfacing to the operator.
    }
  }

  /**
   * The launcher's settings as they would be restored. A list-backed part not yet applied is taken from
   * pendingRunSettings, so a save made before its list arrives keeps the remembered value rather than the
   * placeholder the form holds until then.
   */
  private runSettingsSnapshot(): BenchmarkRunSettings {
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
      completionNotification: this.completionNotification,
      runMode: this.runMode,
      batchModelIds: [...this.batchModelKeys],
      batchOrder: this.batchOrder,
      batchOrderIds: [...this.batchOrderKeys],
      batchRunsPerModel: this.effectiveBatchRunsPerModel
    };

    const pending = this.pendingRunSettings;
    if (pending) {
      const applied = this.runSettingsApplied;
      if (!applied.suite) {
        settings.suiteId = pending.suiteId;
      }
      if (!applied.profile) {
        settings.scoringProfileId = pending.scoringProfileId;
      }
      if (!applied.configs) {
        settings.testedConfigId = pending.testedConfigId;
        settings.assessorConfigId = pending.assessorConfigId;
        settings.coAssessorConfigId = pending.coAssessorConfigId;
        settings.secondOpinionConfigId = pending.secondOpinionConfigId;
        settings.claimVerifierConfigId = pending.claimVerifierConfigId;
        settings.reportWriterConfigId = pending.reportWriterConfigId;
        settings.batchModelIds = pending.batchModelIds ?? null;
        settings.batchOrderIds = pending.batchOrderIds ?? null;
      }
      if (!applied.battery) {
        settings.targetKind = pending.targetKind;
        settings.batteryId = pending.batteryId;
      }
    }
    return settings;
  }

  /** Reads the stored blob into pendingRunSettings, and restores the fields no loader owns. */
  restoreRunSettings(): void {
    this.restoreAttempted = true;
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
    const ids = (v: unknown): number[] | null =>
      Array.isArray(v) ? v.filter((id): id is number => typeof id === 'number' && Number.isInteger(id)) : null;

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
      completionNotification: typeof raw.completionNotification === 'boolean' ? raw.completionNotification : null,
      runMode: raw.runMode === 'batch' || raw.runMode === 'single' ? raw.runMode : null,
      batchModelIds: ids(raw.batchModelIds),
      batchOrder: raw.batchOrder === 'randomized' || raw.batchOrder === 'asListed' ? raw.batchOrder : null,
      batchOrderIds: ids(raw.batchOrderIds),
      batchRunsPerModel: num(raw.batchRunsPerModel)
    };

    // A model batch's mode, order and runs per model need no list; its models wait for the configurations.
    if (this.pendingRunSettings.runMode) {
      this.runMode = this.pendingRunSettings.runMode;
    }
    if (this.pendingRunSettings.batchOrder) {
      this.batchOrder = this.pendingRunSettings.batchOrder;
    }
    const batchRuns = this.pendingRunSettings.batchRunsPerModel ?? null;
    if (batchRuns !== null && batchRuns >= 1) {
      const intRuns = Math.floor(batchRuns);
      const max = this.maxRunCountPerSeries;
      this.batchRunsPerModel = max != null && intRuns > max ? max : intRuns;
    }

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

  // --- Repeat a run's setup ---
  //
  // Repeat this run's setup copies a finished run's recorded selections into the launcher. It waits
  // until the remembered settings have been applied, because a loader applying the stored blob later
  // would overwrite it, and until the lists it validates against are here. A recorded selection that
  // is gone or no longer qualifies leaves the field as it is and adds a note naming it, so nothing is
  // substituted silently. Nothing starts.

  /** Whether restoreRunSettings has run. */
  private restoreAttempted = false;

  /** The lists a prefill validates against that have arrived at least once. */
  private readonly listsArrived = { suite: false, profile: false, configs: false };

  /** The run whose setup waits for prefillReady. */
  private pendingPrefillRun: BenchmarkRunDetailDto | null = null;

  /** The last applied prefill, for the note on the Run Benchmark panel; null when there is none to show. */
  prefillResult: BenchmarkRunPrefillResult | null = null;

  /** The run whose setup is waiting to be applied, or null. */
  get pendingPrefillRunId(): number | null {
    return this.pendingPrefillRun?.id ?? null;
  }

  /**
   * True once the remembered settings were read and every list-backed part of them applied, and the
   * suites, profiles and configurations have arrived.
   */
  get prefillReady(): boolean {
    const lists = this.listsArrived;
    return this.restoreAttempted && this.pendingRunSettings == null
      && lists.suite && lists.profile && lists.configs;
  }

  /** Fills the launcher from the run's recorded setup, now or once prefillReady holds. */
  prefillFromRun(run: BenchmarkRunDetailDto): void {
    this.prefillResult = null;
    this.pendingPrefillRun = run;
    this.applyPendingPrefill();
  }

  /** Hides the prefill note. A prefill still waiting is kept. */
  clearPrefillResult(): void {
    this.prefillResult = null;
  }

  private applyPendingPrefill(): void {
    const run = this.pendingPrefillRun;
    if (!run || !this.prefillReady) return;
    this.pendingPrefillRun = null;
    this.prefillResult = { runId: run.id, notes: this.applyRunSetup(run) };
    // The run's setup replaces the restored selections the notes describe.
    this.restoreNotes = [];
    this.persistRunSettings();
    this.loadLastAssessor();
    this.refreshReusePreview();
    this.viewSync.notify();
  }

  /** Copies the run's recorded selections into the form; returns a note per selection it kept back. */
  private applyRunSetup(run: BenchmarkRunDetailDto): string[] {
    const notes: string[] = [];

    const suiteId = run.benchmarkSuiteId ?? null;
    if (suiteId != null && this.workspace.suites.some(s => s.id === suiteId)) {
      this.selectedSuiteId = suiteId;
    } else {
      notes.push(`Benchmark Suite: ${run.suiteName || 'the run\'s suite'} no longer exists, so the selected suite was kept.`);
    }

    const profileId = run.scoringProfileId ?? null;
    if (profileId != null && this.workspace.scoringProfiles.some(p => p.id === profileId)) {
      this.selectedScoringProfileId = profileId;
    } else if (profileId != null || run.scoringProfileName) {
      notes.push(`Scoring Profile: ${run.scoringProfileName || `profile #${profileId}`} no longer exists, so the selected profile was kept.`);
    }

    const capable = this.workspace.benchmarkCapableConfigs;
    const qualifies = (id: number | null | undefined): id is number => id != null && capable.some(c => c.id === id);
    const unavailable = (role: string, name: string | null | undefined, id: number | null | undefined): string =>
      `${role}: ${name || (id != null ? `configuration #${id}` : 'the recorded model')} is no longer available for benchmark runs, so the current choice was kept.`;

    // The model under test and the assessor are required, so a missing one always keeps the current choice.
    if (qualifies(run.testedModelConfigurationId)) {
      this.testedConfigId = run.testedModelConfigurationId;
    } else {
      notes.push(unavailable('Model Under Test', run.testedModelDisplayNameUsed, run.testedModelConfigurationId));
    }
    if (qualifies(run.assessorModelConfigurationId)) {
      this.assessorConfigId = run.assessorModelConfigurationId;
    } else {
      notes.push(unavailable('Assessor', run.assessorModelDisplayNameUsed, run.assessorModelConfigurationId));
    }

    // An optional role the run did not use is cleared; one it used but that is gone keeps the current choice.
    const optionalRole = (role: string, id: number | null | undefined, name: string | null | undefined,
                          assign: (value: number | null) => void): boolean => {
      if (qualifies(id)) {
        assign(id);
        return true;
      }
      if (id == null && !name) {
        assign(null);
        return true;
      }
      notes.push(unavailable(role, name, id));
      return false;
    };

    optionalRole('Co-Assessor', run.coAssessorModelConfigurationId, run.coAssessorModelDisplayNameUsed,
      v => { this.coAssessorConfigId = v; });
    const secondReaderApplied = optionalRole(run.isPanelRun ? 'Reference Reader' : 'Second Reader',
      run.secondOpinionAssessorModelConfigurationId, run.secondOpinionAssessorModelDisplayNameUsed,
      v => { this.secondOpinionConfigId = v; });
    optionalRole('Claim Verifier', run.claimVerifierModelConfigurationId, run.claimVerifierDisplayNameUsed,
      v => { this.claimVerifierConfigId = v; });
    optionalRole('Report Writer', run.reportWriterModelConfigurationId, run.reportWriterDisplayName,
      v => { this.reportWriterConfigId = v; });

    // Coverage is the run's own only for a single-assessor run with a second reader; a panel run's is
    // forced, and without a second reader it is inert, so both follow the profile again.
    if (secondReaderApplied) {
      const mode = Number(run.secondOpinionModeUsed);
      this.secondOpinionModeOverride =
        !run.isPanelRun && this.secondOpinionConfigId != null && this.secondOpinionModeOptions.some(o => o.value === mode)
          ? mode
          : null;
    }

    const options = parsePromptOptions(run.candidatePromptOptionsJson);
    if (!options) {
      notes.push('Prompt options: the run recorded none, so Response Style and source code references were kept.');
    } else {
      const verbose = options['verboseMode'];
      if (typeof verbose === 'boolean') {
        this.candidateVerboseMode = verbose;
      }
      const sourceReferences = options['allowSourceCodeReferences'];
      if (typeof sourceReferences === 'boolean') {
        this.candidateAllowSourceCodeReferences = sourceReferences;
      }
    }

    this.runCount = 1;
    this.runTargetKind = 'suite';
    this.runMode = 'single';
    return notes;
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

  // --- Model batches ---
  //
  // A model batch runs several models under test one after another under this one settings set. The
  // server judges every guardrail behind POST model-batches/preflight; the launcher asks it once the
  // settings settle and renders what it returns, and Start sends the acknowledged warnings' keys.

  /** One model under test, or a model batch. */
  runMode: BenchmarkLauncherRunMode = 'single';

  /** The chosen models under test, in the picker's option order. */
  batchModelKeys: number[] = [];

  batchOrder: BenchmarkModelBatchOrderChoice = 'randomized';

  /** The chosen models in the As listed order: the run order under As listed. */
  batchOrderKeys: number[] = [];

  /** Runs per model on a single suite; a battery uses Runs per Suite (`runCount`) instead. */
  batchRunsPerModel = 1;

  /** The acknowledgment keys of the warnings the operator ticked. Kept for one start; never stored. */
  acknowledgedFindingKeys = new Set<string>();

  /** The latest check's findings, blockers first as the server orders them. */
  findings: BenchmarkModelBatchFindingDto[] = [];

  projection: BenchmarkModelBatchProjectionDto | null = null;

  /** The most models one batch may run, as the latest check reported it; null before one has answered. */
  maxModelsPerBatch: number | null = null;

  /** A check is pending: debounced, or in flight. */
  preflightLoading = false;

  /** Why the latest check failed; null after a successful one. */
  preflightError: string | null = null;

  /** True once a check has answered for the settings in force. */
  preflightAnswered = false;

  private readonly preflightRequested$ = new Subject<void>();

  private readonly preflightSubscription: Subscription;

  private preflightRetryTimer: ReturnType<typeof setTimeout> | null = null;

  private preflightFailures = 0;

  get isModelBatch(): boolean {
    return this.runMode === 'batch';
  }

  /** Runs per model in force: a non-numeric or out-of-range value resolves to 1, as Number of Runs does. */
  get effectiveBatchRunsPerModel(): number {
    const n = Math.floor(Number(this.batchRunsPerModel));
    if (!Number.isFinite(n) || n < 1) return 1;
    const max = this.maxRunCountPerSeries;
    return max != null && n > max ? max : n;
  }

  /** R as the batch request carries it: runs per model on a suite, runs per suite on a battery. */
  get batchRunsPerMember(): number {
    return this.isBatteryTarget ? this.effectiveRunCount : this.effectiveBatchRunsPerModel;
  }

  /** The models in the order the request lists them: the As listed order, or the picker's under Randomized. */
  get batchRunOrderIds(): number[] {
    return this.batchOrder === 'asListed' ? this.batchOrderKeys : this.batchModelKeys;
  }

  /** The Models Under Test picker's hint in place of the default; null for the default. */
  batchModelsHint: string | null = null;

  /** Run Benchmark focuses the Models Under Test picker once it shows it. */
  batchModelsFocusPending = false;

  /**
   * Fills the launcher as a model batch on this target with this model chosen, as a Chat Consistency
   * suggestion asks; nothing starts. A target or model the lists no longer offer keeps the current
   * choice. The report writer is cleared and earlier runs are not reused, as the Models radios do.
   */
  prefillModelBatch(prefill: ModelBatchPrefill): void {
    this.runMode = 'batch';
    this.reportWriterConfigId = null;
    this.reuseEarlierRuns = false;
    if (prefill.targetKind === 'battery') {
      this.runTargetKind = 'battery';
      if (prefill.batteryId != null && this.runnableBatteries.some(b => b.id === prefill.batteryId)) {
        this.selectedBatteryId = prefill.batteryId;
      } else if (this.selectedBatteryId == null) {
        this.selectedBatteryId = this.runnableBatteries[0]?.id ?? null;
      }
    } else {
      this.runTargetKind = 'suite';
      if (prefill.suiteId != null && this.workspace.suites.some(s => s.id === prefill.suiteId)) {
        this.selectedSuiteId = prefill.suiteId;
      }
    }
    const modelId = prefill.modelConfigurationId;
    if (modelId != null && this.workspace.benchmarkCapableConfigs.some(c => c.id === modelId)) {
      this.setBatchModels([modelId]);
    }
    this.batchModelsHint = prefill.controlSuggested ? 'Add a control model from another provider.' : null;
    this.batchModelsFocusPending = true;
    this.clampRunCountToTarget();
    this.refreshReusePreview();
    this.persistRunSettings();
    this.requestPreflight();
    this.viewSync.notify();
  }

  /** Sets the chosen models; the As listed order keeps the models it had and appends the new ones. */
  setBatchModels(keys: readonly number[]): void {
    this.batchModelKeys = BenchmarkLauncherState.distinct(keys);
    this.batchOrderKeys = BenchmarkLauncherState.mergeOrder(this.batchOrderKeys, this.batchModelKeys);
  }

  /** Sets the As listed order; ids that are not chosen are ignored and chosen ones missing are appended. */
  setBatchOrderKeys(keys: readonly number[]): void {
    this.batchOrderKeys = BenchmarkLauncherState.mergeOrder(keys, this.batchModelKeys);
  }

  /** `order` restricted to `chosen`, followed by the chosen ids it does not hold, in `chosen`'s order. */
  private static mergeOrder(order: readonly number[], chosen: readonly number[]): number[] {
    const kept = BenchmarkLauncherState.distinct(order.filter(id => chosen.includes(id)));
    return [...kept, ...chosen.filter(id => !kept.includes(id))];
  }

  private static distinct(ids: readonly number[]): number[] {
    return ids.filter((id, i) => ids.indexOf(id) === i);
  }

  /** Blockers among the findings. */
  get batchBlockers(): BenchmarkModelBatchFindingDto[] {
    return this.findings.filter(f => f.severity === 'Blocker');
  }

  /** Warnings among the findings. */
  get batchWarnings(): BenchmarkModelBatchFindingDto[] {
    return this.findings.filter(f => f.severity === 'Warning');
  }

  /** Warnings the operator has not acknowledged yet. A warning without a key cannot be acknowledged and does not hold Start. */
  get unacknowledgedBatchWarnings(): BenchmarkModelBatchFindingDto[] {
    return this.batchWarnings.filter(f => !!f.acknowledgmentKey && !this.acknowledgedFindingKeys.has(f.acknowledgmentKey));
  }

  /** The acknowledged warnings, for the confirmation dialog. */
  get acknowledgedBatchWarnings(): BenchmarkModelBatchFindingDto[] {
    return this.batchWarnings.filter(f => !!f.acknowledgmentKey && this.acknowledgedFindingKeys.has(f.acknowledgmentKey));
  }

  setFindingAcknowledged(key: string, acknowledged: boolean): void {
    const next = new Set(this.acknowledgedFindingKeys);
    if (acknowledged) {
      next.add(key);
    } else {
      next.delete(key);
    }
    this.acknowledgedFindingKeys = next;
  }

  /**
   * Takes findings from a check or a refused start. An acknowledgment whose key no longer appears is
   * dropped, so an old tick never covers a new problem: the key changes with the affected models.
   */
  applyFindings(findings: readonly BenchmarkModelBatchFindingDto[]): void {
    this.findings = [...findings];
    const current = new Set(this.batchWarnings.map(f => f.acknowledgmentKey).filter((k): k is string => !!k));
    const kept = [...this.acknowledgedFindingKeys].filter(k => current.has(k));
    if (kept.length !== this.acknowledgedFindingKeys.size) {
      this.acknowledgedFindingKeys = new Set(kept);
    }
  }

  /** The model batch Start would send now; null while a field it needs is unset. */
  buildModelBatchRequest(): StartBenchmarkModelBatchRequest | null {
    const assessorId = this.assessorConfigId;
    if (assessorId == null) return null;
    const ids = this.batchRunOrderIds;
    const templateTestedId = ids[0] ?? this.testedConfigId;
    if (templateTestedId == null) return null;

    const battery = this.isBatteryTarget ? this.selectedBattery : undefined;
    const suiteId = this.launchSuiteId;
    if (suiteId == null || (this.isBatteryTarget && !battery)) return null;

    // Same-provider acknowledgments travel as warning keys; the server sets them per member.
    const run: StartBenchmarkRunRequest = { ...this.buildRunRequest(suiteId, templateTestedId, assessorId), acknowledgeSameProvider: false };
    delete run.acknowledgeSameProviderReportWriter;

    const warningKeys = new Set(this.batchWarnings.map(f => f.acknowledgmentKey));
    return {
      targetKind: battery ? 'Battery' : 'Suite',
      suiteId: battery ? null : suiteId,
      batteryId: battery?.id ?? null,
      testedModelConfigurationIds: [...ids],
      runsPerModel: this.batchRunsPerMember,
      order: this.batchOrder === 'asListed' ? 'AsListed' : 'Randomized',
      allowCapWait: this.allowCapWait,
      run,
      acknowledgedFindingKeys: [...this.acknowledgedFindingKeys].filter(k => warningKeys.has(k))
    };
  }

  /**
   * Asks for a check of the batch settings once they settle. Outside batch mode it clears the
   * findings instead. Every launcher change handler calls it through the run tab's save.
   */
  requestPreflight(): void {
    this.clearPreflightRetry();
    if (!this.isModelBatch) {
      this.findings = [];
      this.projection = null;
      this.preflightLoading = false;
      this.preflightError = null;
      this.preflightAnswered = false;
      return;
    }
    this.preflightLoading = true;
    this.preflightAnswered = false;
    this.preflightRequested$.next();
  }

  private runPreflight(): Observable<ModelBatchPreflightOutcome> {
    const req = this.isModelBatch ? this.buildModelBatchRequest() : null;
    if (!req) {
      return of({ kind: 'none' } as const);
    }
    return this.benchmarkService.preflightModelBatch(req).pipe(
      map(response => ({ kind: 'ok', response }) as const),
      catchError(err => of({ kind: 'error', message: refusalText(err, 'The server could not check the batch.') } as const))
    );
  }

  private applyPreflightOutcome(outcome: ModelBatchPreflightOutcome): void {
    this.preflightLoading = false;
    // An answer that lands after a switch to One model is about settings no longer shown.
    if (!this.isModelBatch) {
      return;
    }
    switch (outcome.kind) {
      case 'none':
        this.applyFindings([]);
        this.projection = null;
        this.preflightError = null;
        this.preflightAnswered = false;
        this.preflightFailures = 0;
        break;
      case 'ok':
        this.applyFindings(outcome.response.findings ?? []);
        this.projection = outcome.response.projection ?? null;
        this.maxModelsPerBatch = outcome.response.maxModels ?? this.maxModelsPerBatch;
        this.preflightError = null;
        this.preflightAnswered = true;
        this.preflightFailures = 0;
        break;
      case 'error': {
        this.preflightError = outcome.message;
        this.preflightAnswered = false;
        // Backs off rather than hammering a failing endpoint; a settings change asks again at once.
        const delay = Math.min(MODEL_BATCH_PREFLIGHT_RETRY_MAX_MS, MODEL_BATCH_PREFLIGHT_RETRY_MS * 2 ** this.preflightFailures);
        this.preflightFailures++;
        this.preflightRetryTimer = setTimeout(() => {
          this.preflightRetryTimer = null;
          this.requestPreflight();
        }, delay);
        break;
      }
    }
    this.viewSync.notify();
  }

  private clearPreflightRetry(): void {
    if (this.preflightRetryTimer != null) {
      clearTimeout(this.preflightRetryTimer);
      this.preflightRetryTimer = null;
    }
  }

  ngOnDestroy(): void {
    this.reusePreviewSubscription?.unsubscribe();
    this.reusePreviewSubscription = null;
    this.preflightSubscription.unsubscribe();
    this.clearPreflightRetry();
  }
}
