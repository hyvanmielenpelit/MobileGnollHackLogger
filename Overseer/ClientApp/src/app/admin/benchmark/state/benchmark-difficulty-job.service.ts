import { OnDestroy, inject, Injectable } from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  DifficultyAssessmentJobDto,
  DifficultyAssessmentJobItemDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';
import { BenchmarkViewSync } from './benchmark-view-sync.service';

/** The difficulty assessment job and its polling, shared by the assessor dialog, Manage Suites and Manage Questions. */
@Injectable()
export class BenchmarkDifficultyJobService implements OnDestroy {
  private readonly viewSync = inject(BenchmarkViewSync);
  private readonly workspace = inject(BenchmarkWorkspaceStore);
  private benchmarkService = inject(AdminBenchmarkService);


  difficultyJob: DifficultyAssessmentJobDto | null = null;

  terminatingDifficultyJob = false;

  private difficultyPollInterval: any = null;

  private visibilityChangeHandler: (() => void) | null = null;

  get difficultyJobIsRunning(): boolean {
    return this.difficultyJob != null && this.difficultyJob.status === 'Running';
  }

  get difficultyJobIsTerminal(): boolean {
    return this.difficultyJob != null && this.difficultyJob.status !== 'Running';
  }

  get difficultyProgressValue(): number {
    if (!this.difficultyJob) return 0;
    return this.difficultyJob.ratedCount + this.difficultyJob.failedCount;
  }

  get difficultyProgressMax(): number {
    return this.difficultyJob?.totalCount || 100;
  }

  get failedDifficultyItems(): DifficultyAssessmentJobItemDto[] {
    return this.difficultyJob?.items.filter(i => i.status === 'Failed') || [];
  }

  checkActiveDifficultyAssessment(): void {
    this.benchmarkService.getActiveDifficultyAssessment().subscribe({
      next: (job) => {
        if (job) {
          this.difficultyJob = job;
          this.terminatingDifficultyJob = false;
          if (job.status === 'Running') {
            this.startDifficultyPolling(job.id);
          }
          this.viewSync.notify();
        }
      },
      error: (err) => console.error('Failed to check active difficulty assessment', err)
    });
  }

  /** Whether the difficulty assessor dialog AdminBenchmarkComponent hosts is open. */
  isDifficultyAssessorDialogOpen = false;

  startDifficultyPolling(jobId: string) {
    this.stopDifficultyPolling();

    this.pollDifficultyJob(jobId);

    this.difficultyPollInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.pollDifficultyJob(jobId);
    }, 1500);

    if (typeof document !== 'undefined') {
      this.visibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollDifficultyJob(jobId);
        }
      };
      document.addEventListener('visibilitychange', this.visibilityChangeHandler);
    }
  }

  private pollDifficultyJob(jobId: string) {
    this.benchmarkService.getDifficultyAssessment(jobId).subscribe({
      next: (job) => {
        this.difficultyJob = job;
        if (job.status !== 'Running') {
          this.terminatingDifficultyJob = false;
          this.stopDifficultyPolling();
          this.workspace.loadSuites();
          if (this.workspace.currentSuiteForQuestions) {
            this.workspace.loadQuestions(this.workspace.currentSuiteForQuestions.id);
          }
        }
        this.viewSync.notify();
      },
      error: (err) => {
        console.error('Failed to poll difficulty job', err);
      }
    });
  }

  stopDifficultyPolling() {
    if (this.difficultyPollInterval) {
      clearInterval(this.difficultyPollInterval);
      this.difficultyPollInterval = null;
    }
    if (this.visibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityChangeHandler);
      this.visibilityChangeHandler = null;
    }
  }

  terminateDifficultyAssessment() {
    if (!this.difficultyJob) return;
    // Stays set until a poll reports the job has left Running.
    this.terminatingDifficultyJob = true;
    this.benchmarkService.cancelDifficultyAssessment(this.difficultyJob.id).subscribe({
      next: () => {
        this.pollDifficultyJob(this.difficultyJob!.id);
        this.viewSync.notify();
      },
      error: (err) => {
        this.terminatingDifficultyJob = false;
        this.workspace.actionErrorMessage = err?.error || 'Failed to cancel assessment.';
        this.viewSync.notify();
      }
    });
  }

  difficultyProgressLabel(suite?: BenchmarkSuiteDto | null): string {
    if (suite) {
      return `Difficulty ${suite.assessedQuestionCount}/${suite.questionCount} Assessed`;
    }
    if (!this.difficultyJob) return '';
    const total = this.difficultyJob.totalCount;
    const rated = this.difficultyJob.ratedCount;
    const failed = this.difficultyJob.failedCount;
    if (this.difficultyJob.status === 'Cancelled') {
      return `Assessment canceled. Rated ${rated} of ${total} questions.`;
    }
    if (this.difficultyJob.status === 'Failed') {
      return `Assessment failed. Rated ${rated} of ${total} questions.`;
    }
    if (failed > 0) {
      return `Rated ${rated} of ${total} questions (${failed} failed).`;
    }
    return `Rated ${rated} of ${total} questions.`;
  }

  difficultyProgressClass(suite: BenchmarkSuiteDto): string {
    if (suite.difficultyFullyAssessed) return 'complete';
    if (suite.assessedQuestionCount === 0) return 'none';
    return 'partial';
  }

  ngOnDestroy(): void {
    this.stopDifficultyPolling();
    this.terminatingDifficultyJob = false;
  }
}
