import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';
import { BenchmarkQuestionDto, BenchmarkSuiteDto, RecommendedRun } from '../../../services/admin-benchmark.service';
import { GraderGuideProfile, GraderGuideSection } from '../grader-guide/benchmark-grader-guide.component';

/** The Benchmark tab's sub-tabs, in tab order. */
export type BenchmarkSubTab =
  'run' | 'history' | 'multirun' | 'multisuite' | 'suites' | 'profiles' | 'modelcomparison' | 'chatconsistency';

/** What the shell's confirmation dialog asks; the button defaults to a destructive Delete. */
export interface BenchmarkConfirmOptions {
  title: string;
  message: string;
  dangerNotice?: string;
  buttonText?: string;
  buttonClass?: string;
  icon?: 'delete' | 'none';
  action: () => void;
}

/** A request for the difficulty assessor dialog: a suite, and one question to rate, or none for the suite. */
export interface BenchmarkDifficultyAssessorRequest {
  suite?: BenchmarkSuiteDto | null;
  question: BenchmarkQuestionDto | null;
}

/** A request for the grader guide; an undefined profile prints the launcher's selected profile. */
export interface BenchmarkGraderGuideRequest {
  section: GraderGuideSection;
  profile?: GraderGuideProfile | null;
}

/**
 * What the comparison wizard opens with selected; it opens on step 1. Battery results are never
 * selected beside runs or groups: when `batteryRunIds` is not empty, `runIds` and `groupIds` are ignored.
 */
export interface ComparisonWizardPreset {
  batteryRunIds: readonly number[];
  runIds?: readonly number[];
  groupIds?: readonly number[];
  /** Why the GnollBench baseline model's result is not among the sources; shown once in the wizard's notice band. */
  baselineModelNote?: string | null;
}

/** What Run Benchmark's model batch launcher is filled with: the target and the first model. Nothing starts. */
export interface ModelBatchPrefill {
  targetKind: 'suite' | 'battery';
  suiteId: number | null;
  batteryId: number | null;
  /** The model under test to select first; null when it is not known. */
  modelConfigurationId: number | null;
  /** The picker's hint asks for a control model from another provider. */
  controlSuggested: boolean;
}

/** The settings of a recommended run the launcher takes over, named as the server names them. */
export type LauncherSetupFields = Pick<RecommendedRun,
  'kind' | 'targetKind' | 'suiteId' | 'suiteName' | 'batteryId' | 'batteryName' | 'runMode' |
  'testedModelConfigurationIds' | 'modelNames' | 'order' | 'runsPerModel' | 'runCount' | 'scoringProfileId' |
  'verboseMode' | 'allowSourceCodeReferences' | 'assessorModelConfigurationId' | 'coAssessorModelConfigurationId' |
  'secondOpinionAssessorModelConfigurationId' | 'secondOpinionMode' | 'claimVerifierModelConfigurationId' | 'notes'>;

/**
 * A recommended run's launcher settings, as a Root Cause Analysis or a control-run suggestion gives
 * them, and the heading of the note that says where they came from. Nothing starts.
 */
export interface LauncherSetupPrefill extends LauncherSetupFields {
  /** The heading of the launcher's note: *Set up from Root Cause Analysis #12, run 2*. */
  sourceLabel: string;
  /** The information condition's wire name; a recommended run is always `production`. Absent means production. */
  informationCondition?: string | null;
  /** The instruction variant's name; a recommended run never has one. */
  instructionVariant?: string | null;
}

/** The launcher settings of a recommended run, with the note heading `sourceLabel`. */
export function launcherSetupFromRecommendedRun(run: RecommendedRun, sourceLabel: string): LauncherSetupPrefill {
  return {
    sourceLabel,
    kind: run.kind,
    targetKind: run.targetKind,
    suiteId: run.suiteId ?? null,
    suiteName: run.suiteName ?? null,
    batteryId: run.batteryId ?? null,
    batteryName: run.batteryName ?? null,
    runMode: run.runMode,
    testedModelConfigurationIds: [...(run.testedModelConfigurationIds ?? [])],
    modelNames: [...(run.modelNames ?? [])],
    order: run.order,
    runsPerModel: run.runsPerModel,
    runCount: run.runCount,
    scoringProfileId: run.scoringProfileId ?? null,
    verboseMode: run.verboseMode ?? null,
    allowSourceCodeReferences: run.allowSourceCodeReferences ?? null,
    assessorModelConfigurationId: run.assessorModelConfigurationId ?? null,
    coAssessorModelConfigurationId: run.coAssessorModelConfigurationId ?? null,
    secondOpinionAssessorModelConfigurationId: run.secondOpinionAssessorModelConfigurationId ?? null,
    secondOpinionMode: run.secondOpinionMode ?? null,
    claimVerifierModelConfigurationId: run.claimVerifierModelConfigurationId ?? null,
    // A Root Cause Analysis recommends production runs only.
    informationCondition: 'production',
    instructionVariant: null,
    notes: [...(run.notes ?? [])]
  };
}

/**
 * Requests from the sub-tabs and the state services to the dialogs and the tab row AdminBenchmarkComponent
 * hosts. The methods keep the names the shell's own handlers have, so a template reads the same either way.
 */
@Injectable()
export class BenchmarkShellBridge {
  readonly selectSubTab$ = new Subject<BenchmarkSubTab>();
  readonly openRunReport$ = new Subject<number>();
  readonly openRunProgress$ = new Subject<boolean>();
  readonly openDifficultyAssessor$ = new Subject<BenchmarkDifficultyAssessorRequest>();
  readonly openSnapshotViewer$ = new Subject<number>();
  readonly closeSnapshotViewerFor$ = new Subject<number>();
  readonly openGraderGuide$ = new Subject<BenchmarkGraderGuideRequest>();
  readonly confirm$ = new Subject<BenchmarkConfirmOptions>();
  readonly openComparisonWizard$ = new Subject<ComparisonWizardPreset | null>();
  readonly openBatteryRunReport$ = new Subject<number>();
  readonly runDeleted$ = new Subject<number>();
  /** A run id whose setup the Run Benchmark launcher takes over. */
  readonly repeatRunSetup$ = new Subject<number>();
  /** A model batch setup the Run Benchmark launcher takes over. */
  readonly prefillModelBatch$ = new Subject<ModelBatchPrefill>();
  /** A recommended run's settings the Run Benchmark launcher takes over. */
  readonly prefillSetup$ = new Subject<LauncherSetupPrefill>();

  private leaveGuard: (() => string | null) | null = null;

  /** Registers why the active sub-tab cannot be left now (null while it can); returns the release. */
  setLeaveGuard(guard: () => string | null): () => void {
    this.leaveGuard = guard;
    return () => {
      if (this.leaveGuard === guard) this.leaveGuard = null;
    };
  }

  /** Why switching away from the active sub-tab would break work in progress, or null. */
  leaveRefusal(): string | null {
    return this.leaveGuard?.() ?? null;
  }

  selectSubTab(tab: BenchmarkSubTab): void {
    this.selectSubTab$.next(tab);
  }

  viewRunDetail(runId: number): void {
    this.openRunReport$.next(runId);
  }

  openRunProgressDialog(fromSeries = false): void {
    this.openRunProgress$.next(fromSeries);
  }

  openDifficultyAssessorDialog(suite?: BenchmarkSuiteDto | null, question: BenchmarkQuestionDto | null = null): void {
    this.openDifficultyAssessor$.next({ suite, question });
  }

  openSnapshotViewer(snapshotId: number): void {
    this.openSnapshotViewer$.next(snapshotId);
  }

  /** Closes the snapshot viewer if it shows this snapshot. */
  closeSnapshotViewerFor(snapshotId: number): void {
    this.closeSnapshotViewerFor$.next(snapshotId);
  }

  openGraderGuide(section: GraderGuideSection, profile?: GraderGuideProfile | null): void {
    this.openGraderGuide$.next({ section, profile });
  }

  openConfirmDialog(options: BenchmarkConfirmOptions): void {
    this.confirm$.next(options);
  }

  /** Opens the comparison wizard, with `preset`'s sources selected when given. */
  openComparisonWizard(preset: ComparisonWizardPreset | null = null): void {
    this.openComparisonWizard$.next(preset);
  }

  /** Opens the Battery Run Report over whatever is showing. */
  openBatteryRunReport(batteryRunId: number): void {
    this.openBatteryRunReport$.next(batteryRunId);
  }

  /** A run was deleted; the run report closes if it shows that run. */
  runDeleted(runId: number): void {
    this.runDeleted$.next(runId);
  }

  /** Opens Run Benchmark with this run's setup filled in; nothing starts. */
  repeatRunSetup(runId: number): void {
    this.repeatRunSetup$.next(runId);
  }

  /** Opens Run Benchmark as a model batch with this target and model; nothing starts. */
  prefillModelBatch(prefill: ModelBatchPrefill): void {
    this.prefillModelBatch$.next(prefill);
  }

  /** Opens Run Benchmark with a recommended run's settings filled in, under the note heading `sourceLabel`; nothing starts. */
  prefillSetup(setup: RecommendedRun, sourceLabel: string): void {
    this.prefillSetup$.next(launcherSetupFromRecommendedRun(setup, sourceLabel));
  }
}
