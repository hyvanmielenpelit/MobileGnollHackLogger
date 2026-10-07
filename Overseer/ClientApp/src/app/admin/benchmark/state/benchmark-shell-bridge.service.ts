import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';
import { BenchmarkQuestionDto, BenchmarkSuiteDto } from '../../../services/admin-benchmark.service';
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

/** What the comparison wizard opens with selected; it opens on step 1. */
export interface ComparisonWizardPreset {
  batteryRunIds: readonly number[];
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
}
