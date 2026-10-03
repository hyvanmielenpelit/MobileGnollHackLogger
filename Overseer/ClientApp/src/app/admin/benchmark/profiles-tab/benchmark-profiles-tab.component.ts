import { Component, ChangeDetectorRef, ViewChild, ElementRef, OnInit, inject } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkScoringProfileDto,
  CreateBenchmarkScoringProfileRequest,
  UpdateBenchmarkScoringProfileRequest,
  BenchmarkSecondOpinionMode,
  BENCHMARK_SECOND_OPINION_MODES
} from '../../../services/admin-benchmark.service';
import { GraderGuideSection } from '../grader-guide/benchmark-grader-guide.component';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { BenchmarkLauncherState } from '../state/benchmark-launcher.state';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** The Scoring Profiles sub-tab and its profile form. */
@Component({
  selector: 'app-benchmark-profiles-tab',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, FormsModule
  ],
  templateUrl: './benchmark-profiles-tab.component.html',
  styleUrls: ['./benchmark-profiles-tab.component.scss']
})
export class BenchmarkProfilesTabComponent implements OnInit {
  private readonly viewSync = inject(BenchmarkViewSync);
  readonly bridge = inject(BenchmarkShellBridge);
  readonly workspace = inject(BenchmarkWorkspaceStore);
  readonly launcher = inject(BenchmarkLauncherState);
  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);
  readonly secondOpinionModeOptions = BENCHMARK_SECOND_OPINION_MODES;

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.workspace.loadProfiles();
  }

  /** Opens the grader guide with the launcher's selected profile, or the values of the profile being edited. */
  openGraderGuide(section: GraderGuideSection, fromProfileForm = false): void {
    if (!fromProfileForm) {
      this.bridge.openGraderGuide(section);
      return;
    }
    // The form does not edit the minimum sample, so the stored profile's value is carried over.
    this.bridge.openGraderGuide(section, {
      ...this.profileForm,
      secondOpinionMinimumSample: this.workspace.scoringProfiles.find(p => p.id === this.editingProfileId)?.secondOpinionMinimumSample
    });
  }

  @ViewChild('scoringProfileFormDialog') scoringProfileFormDialog!: ElementRef<HTMLDialogElement>;

  editingProfileId: number | null = null;

  profileForm: CreateBenchmarkScoringProfileRequest = {
    name: '',
    isDefault: false,
    weightAccuracy: 0.55,
    weightCompleteness: 0.25,
    weightConciseness: 0.10,
    weightReadability: 0.10,
    levelScoresJson: '[1, 15, 35, 55, 72, 87, 100]',
    criticalErrorCeiling: 25,
    notAttemptedScore: 50,
    secondOpinionQualityThreshold: 50,
    secondOpinionMode: BenchmarkSecondOpinionMode.Flagged,
    secondOpinionOutlierDeltaPoints: 25,
    secondOpinionBlind: true,
    speedTargetMs: 2000,
    speedDecayK: 12.0,
    speedDifficultyScaling: 1.0,
    maxParallelQuestions: 1
  };

  profileValidationErrors: string[] = [];

  /** The outlier sweep is the only thing the delta configures, so nothing else enables it. */
  get outlierDeltaEnabled(): boolean {
    return this.profileForm.secondOpinionMode === BenchmarkSecondOpinionMode.FlaggedAndOutliers;
  }

  openCreateProfile() {
    this.editingProfileId = null;
    this.profileValidationErrors = [];
    this.profileForm = {
      name: '',
      isDefault: false,
      weightAccuracy: 0.55,
      weightCompleteness: 0.25,
      weightConciseness: 0.10,
      weightReadability: 0.10,
      levelScoresJson: '[1, 15, 35, 55, 72, 87, 100]',
      criticalErrorCeiling: 25,
      notAttemptedScore: 50,
      secondOpinionQualityThreshold: 50,
      secondOpinionMode: BenchmarkSecondOpinionMode.Flagged,
      secondOpinionOutlierDeltaPoints: 25,
      secondOpinionBlind: true,
      speedTargetMs: 15000,
      speedDecayK: 20.0,
      speedDifficultyScaling: 1.0,
      maxParallelQuestions: 1
    };
    this.scoringProfileFormDialog?.nativeElement.showModal();
  }

  openEditProfile(profile: BenchmarkScoringProfileDto) {
    this.editingProfileId = profile.id;
    this.profileValidationErrors = [];
    this.profileForm = {
      name: profile.name,
      isDefault: profile.isDefault,
      weightAccuracy: profile.weightAccuracy,
      weightCompleteness: profile.weightCompleteness,
      weightConciseness: profile.weightConciseness,
      weightReadability: profile.weightReadability,
      levelScoresJson: profile.levelScoresJson,
      criticalErrorCeiling: profile.criticalErrorCeiling,
      notAttemptedScore: profile.notAttemptedScore ?? null,
      secondOpinionQualityThreshold: profile.secondOpinionQualityThreshold ?? 50,
      secondOpinionMode: profile.secondOpinionMode ?? BenchmarkSecondOpinionMode.Flagged,
      secondOpinionOutlierDeltaPoints: profile.secondOpinionOutlierDeltaPoints ?? 25,
      secondOpinionBlind: profile.secondOpinionBlind ?? true,
      speedTargetMs: profile.speedTargetMs,
      speedDecayK: profile.speedDecayK,
      speedDifficultyScaling: profile.speedDifficultyScaling,
      maxParallelQuestions: profile.maxParallelQuestions
    };
    this.scoringProfileFormDialog?.nativeElement.showModal();
  }

  saveProfile() {
    this.profileValidationErrors = [];
    if (!this.profileForm.name.trim()) {
      this.profileValidationErrors.push('Profile name is required.');
      return;
    }

    // Mirrors the server-side range in BenchmarkScoringProfileService.ValidateProfile, so a
    // plainly out-of-range value is reported without a round trip. The server remains the
    // authority; everything else on this form is validated there only.
    const scaling = this.profileForm.speedDifficultyScaling;
    if (scaling == null || !isFinite(scaling) || scaling < 0 || scaling > 5) {
      this.profileValidationErrors.push('Speed difficulty scaling must be between 0.0 and 5.0.');
      return;
    }

    // 0 is meaningful: it disables the score trigger and leaves second opinions to critical
    // errors alone. Mirrors BenchmarkScoringProfileService.ValidateProfile.
    const threshold = this.profileForm.secondOpinionQualityThreshold;
    if (threshold == null || threshold < 0 || threshold > 100) {
      this.profileValidationErrors.push('Second reader threshold must be between 0 and 100.');
      return;
    }

    // Only meaningful under FlaggedAndOutliers, and a zero there would disable the sweep while
    // the mode claims to run it. Mirrors BenchmarkScoringProfileService.ValidateProfile.
    if (this.profileForm.secondOpinionMode === BenchmarkSecondOpinionMode.FlaggedAndOutliers) {
      const delta = this.profileForm.secondOpinionOutlierDeltaPoints;
      if (delta == null || delta <= 0 || delta > 100) {
        this.profileValidationErrors.push('Outlier delta must be between 1 and 100 when the second reader coverage is "Flagged answers and statistical outliers".');
        return;
      }
    }

    // Blank is meaningful: no not-attempted floor. Always sent, because an update that omits it
    // clears it. Mirrors BenchmarkScoringProfileService.ValidateProfile.
    const notAttemptedScore = this.profileForm.notAttemptedScore ?? null;
    if (notAttemptedScore != null && (!Number.isInteger(notAttemptedScore) || notAttemptedScore < 0 || notAttemptedScore > 100)) {
      this.profileValidationErrors.push('Not-attempted score must be blank or a whole number between 0 and 100.');
      return;
    }
    this.profileForm.notAttemptedScore = notAttemptedScore;

    if (this.editingProfileId) {
      this.benchmarkService.updateScoringProfile(this.editingProfileId, this.profileForm as UpdateBenchmarkScoringProfileRequest).subscribe({
        next: () => {
          this.scoringProfileFormDialog?.nativeElement.close();
          this.workspace.loadProfiles();
        },
        error: (err) => {
          if (err?.error?.errors) {
            this.profileValidationErrors = err.error.errors;
          } else {
            this.profileValidationErrors = [err?.error || 'Failed to update profile.'];
          }
          this.cdr.detectChanges();
        }
      });
    } else {
      this.benchmarkService.createScoringProfile(this.profileForm).subscribe({
        next: (created) => {
          this.scoringProfileFormDialog?.nativeElement.close();
          this.workspace.loadProfiles();
          this.launcher.selectedScoringProfileId = created.id;
        },
        error: (err) => {
          if (err?.error?.errors) {
            this.profileValidationErrors = err.error.errors;
          } else {
            this.profileValidationErrors = [err?.error || 'Failed to create profile.'];
          }
          this.cdr.detectChanges();
        }
      });
    }
  }

  setDefaultProfile(profileId: number) {
    this.benchmarkService.setDefaultScoringProfile(profileId).subscribe({
      next: () => this.workspace.loadProfiles(),
      error: (err) => console.error('Failed to set default profile', err)
    });
  }

  deleteProfile(profileId: number) {
    const profile = this.workspace.scoringProfiles.find(p => p.id === profileId);
    const name = profile ? `"${profile.name}"` : 'this scoring profile';
    this.bridge.openConfirmDialog({
      title: 'Delete Scoring Profile',
      message: `Are you sure you want to delete ${name}?`,
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Profile',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteScoringProfile(profileId).subscribe({
          next: () => this.workspace.loadProfiles(),
          error: (err) => console.error('Failed to delete profile', err)
        });
      }
    });
  }
}
