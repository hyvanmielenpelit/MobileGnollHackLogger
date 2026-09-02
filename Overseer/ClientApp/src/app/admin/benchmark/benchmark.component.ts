import { Component, OnInit, OnDestroy, OnChanges, SimpleChanges, Input, ChangeDetectorRef, HostListener, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkQuestionDto,
  BenchmarkRunSummaryDto,
  BenchmarkRunDetailDto,
  BenchmarkScoringProfileDto,
  CreateBenchmarkSuiteRequest,
  UpdateBenchmarkSuiteRequest,
  CreateBenchmarkQuestionRequest,
  UpdateBenchmarkQuestionRequest,
  CreateBenchmarkScoringProfileRequest,
  UpdateBenchmarkScoringProfileRequest,
  StartBenchmarkRunRequest,
  SameProviderWarningDto,
  RateSuiteDifficultyResultDto,
  BenchmarkFootprintDto
} from '../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../services/admin.service';

import { CollapsibleMarkdownComponent } from '../../shared/collapsible-markdown/collapsible-markdown.component';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';

@Component({
  selector: 'app-admin-benchmark',
  standalone: true,
  imports: [CommonModule, FormsModule, CollapsibleMarkdownComponent],
  templateUrl: './benchmark.component.html',
  styleUrls: ['./benchmark.component.scss']
})
export class AdminBenchmarkComponent implements OnInit, OnDestroy, OnChanges {
  @Input() systemConfigs: SystemAiConfigDto[] = [];

  @ViewChild('suiteDialog') suiteDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('questionsDialog') questionsDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('questionFormDialog') questionFormDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('runDetailDialog') runDetailDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('scoringProfilesDialog') scoringProfilesDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('scoringProfileFormDialog') scoringProfileFormDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('sameProviderDialog') sameProviderDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('bulkDeleteDialog') bulkDeleteDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('confirmActionDialog') confirmActionDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('difficultyAssessorDialog') difficultyAssessorDialog!: ElementRef<HTMLDialogElement>;

  // Confirm Action Dialog State
  confirmDialogTitle = '';
  confirmDialogMessage = '';
  confirmDialogDangerNotice = '';
  confirmDialogButtonText = 'Delete';
  confirmDialogButtonClass = 'btn-gh btn-gh-delete';
  /**
   * Whether the confirm dialog's affirmative button carries a trash icon.
   * 'none' for a plain confirmation, where the label alone is clearer than a
   * generic tick — see the frontend_ui_controls skill on when an icon earns
   * its place.
   */
  confirmDialogIcon: 'delete' | 'none' = 'delete';
  private pendingConfirmAction: (() => void) | null = null;

  private benchmarkService = inject(AdminBenchmarkService);
  private cdr = inject(ChangeDetectorRef);

  activeSubTab: 'run' | 'history' | 'suites' = 'run';

  /** Tab order, and the source of truth for arrow-key navigation indices. */
  readonly subTabs = ['run', 'history', 'suites'] as const;

  // Suites
  suites: BenchmarkSuiteDto[] = [];
  selectedSuiteId: number | null = null;
  loadingSuites = false;

  // Scoring Profiles
  scoringProfiles: BenchmarkScoringProfileDto[] = [];
  selectedScoringProfileId: number | null = null;
  loadingProfiles = false;
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
    speedTargetMs: 5000,
    speedDecayK: 25.0,
    maxParallelQuestions: 1
  };
  profileValidationErrors: string[] = [];

  // Run Setup
  testedConfigId: number | null = null;
  assessorConfigId: number | null = null;
  isTestedModelDropdownOpen = false;
  isAssessorModelDropdownOpen = false;
  startingRun = false;
  runErrorMessage: string | null = null;
  sameProviderWarning: SameProviderWarningDto | null = null;

  // Stored Footprint & Bulk Deletion
  footprints: { [suiteId: number]: BenchmarkFootprintDto } = {};
  suiteForBulkDelete: BenchmarkSuiteDto | null = null;
  deletingSuiteRuns = false;

  // Active Run Tracking
  activeRunId: number | null = null;
  activeRunDetail: BenchmarkRunDetailDto | null = null;
  private pollInterval: any = null;

  // History
  historyRuns: BenchmarkRunSummaryDto[] = [];
  historySuiteFilter: number | null = null;
  loadingHistory = false;

  // Detail Modal
  selectedRunDetail: BenchmarkRunDetailDto | null = null;
  loadingDetail = false;
  expandedQuestions = new Set<number>();
  expandedThoughts = new Set<number>();
  rescoringRun = false;
  reassessingAnswerId: number | null = null;

  // Suite Dialogs
  editingSuiteId: number | null = null;
  suiteForm: CreateBenchmarkSuiteRequest = { name: '', description: '' };

  // Questions Dialog
  currentSuiteForQuestions: BenchmarkSuiteDto | null = null;
  questions: BenchmarkQuestionDto[] = [];
  loadingQuestions = false;
  ratingDifficulty = false;
  ratingQuestionId: number | null = null;

  // Difficulty Assessor Dialog
  suiteForDifficultyAssessment: BenchmarkSuiteDto | null = null;
  difficultyAssessorConfigId: number | null = null;
  isDifficultyAssessorDropdownOpen = false;
  difficultyAssessmentScope: 'suite' | 'question' = 'suite';
  questionIdForDifficultyAssessment: number | null = null;

  // Question Form Dialog
  editingQuestionId: number | null = null;
  questionForm: CreateBenchmarkQuestionRequest = { questionText: '', difficulty: 1, expectedPoints: '' };

  ngOnInit() {
    ensureOverlayPolyfills();
    this.loadSuites();
    this.loadProfiles();
    this.loadHistory();
    this.setDefaultModelSelections();
  }

  /**
   * Switches the visible sub-tab and loads whatever that panel needs. The data
   * loads live here rather than in the template so the tab row carries one
   * statement per handler.
   */
  selectSubTab(tab: 'run' | 'history' | 'suites'): void {
    this.activeSubTab = tab;
    if (tab === 'history') {
      this.loadHistory();
    }
    if (tab === 'suites') {
      this.loadSuites();
    }
  }

  /**
   * Roving-tabindex keyboard support required by role="tablist": Left/Right
   * move between tabs and wrap around, Home/End jump to the ends. Enter and
   * Space need no handling because each tab is a real <button>.
   *
   * Focus follows selection in the same turn, so the tab that just became
   * tabindex="0" is the one holding focus.
   */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: this.subTabs.length - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) {
      return;
    }

    event.preventDefault();
    const next = (requested + this.subTabs.length) % this.subTabs.length;
    const tab = this.subTabs[next];
    this.selectSubTab(tab);
    document.getElementById(`bm-tab-${tab}`)?.focus();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['systemConfigs']) {
      this.setDefaultModelSelections();
    }
  }

  ngOnDestroy() {
    this.stopPolling();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    const target = event.target as HTMLElement;
    if (this.isTestedModelDropdownOpen && !target.closest('.tested-model-selector')) {
      this.isTestedModelDropdownOpen = false;
    }
    if (this.isAssessorModelDropdownOpen && !target.closest('.assessor-model-selector')) {
      this.isAssessorModelDropdownOpen = false;
    }
    if (this.isDifficultyAssessorDropdownOpen && !target.closest('.difficulty-assessor-model-selector')) {
      this.isDifficultyAssessorDropdownOpen = false;
    }
  }

  get benchmarkCapableConfigs(): SystemAiConfigDto[] {
    return this.systemConfigs.filter(c => (c.modelRole & 4) === 4 && c.hasApiKey && c.isEnabled);
  }

  get selectedTestedModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.testedConfigId);
  }

  get selectedAssessorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.assessorConfigId);
  }

  get selectedDifficultyAssessorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.difficultyAssessorConfigId);
  }

  toggleTestedModelDropdown(event: Event) {
    event.stopPropagation();
    this.isTestedModelDropdownOpen = !this.isTestedModelDropdownOpen;
    if (this.isTestedModelDropdownOpen) {
      this.isAssessorModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
    }
  }

  toggleAssessorModelDropdown(event: Event) {
    event.stopPropagation();
    this.isAssessorModelDropdownOpen = !this.isAssessorModelDropdownOpen;
    if (this.isAssessorModelDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
    }
  }

  toggleDifficultyAssessorDropdown(event: Event) {
    event.stopPropagation();
    this.isDifficultyAssessorDropdownOpen = !this.isDifficultyAssessorDropdownOpen;
    if (this.isDifficultyAssessorDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isAssessorModelDropdownOpen = false;
    }
  }

  selectTestedModel(config: SystemAiConfigDto) {
    this.testedConfigId = config.id;
    this.isTestedModelDropdownOpen = false;
  }

  selectAssessorModel(config: SystemAiConfigDto) {
    this.assessorConfigId = config.id;
    this.isAssessorModelDropdownOpen = false;
  }

  selectDifficultyAssessorModel(config: SystemAiConfigDto) {
    this.difficultyAssessorConfigId = config.id;
    this.isDifficultyAssessorDropdownOpen = false;
  }

  formatThinkingLevel(level: string | null | undefined): string {
    if (!level) return 'Default';
    return level.charAt(0).toUpperCase() + level.slice(1);
  }

  showReasoningBadge(mode: string | null | undefined): boolean {
    if (!mode) return false;
    const lower = mode.toLowerCase();
    return lower !== 'default' && lower !== 'standard';
  }

  private setDefaultModelSelections() {
    const benchmarkModels = this.benchmarkCapableConfigs;
    if (benchmarkModels.length > 0) {
      if (!this.testedConfigId || !benchmarkModels.some(m => m.id === this.testedConfigId)) {
        this.testedConfigId = benchmarkModels[0].id;
      }
      if (!this.assessorConfigId || !benchmarkModels.some(m => m.id === this.assessorConfigId)) {
        this.assessorConfigId = benchmarkModels[0].id;
      }
    } else {
      this.testedConfigId = null;
      this.assessorConfigId = null;
    }
  }

  // --- Scoring Profiles Management ---

  loadProfiles() {
    this.loadingProfiles = true;
    this.benchmarkService.getScoringProfiles().subscribe({
      next: (data) => {
        this.scoringProfiles = data;
        this.loadingProfiles = false;
        const defaultProf = this.scoringProfiles.find(p => p.isDefault);
        if (defaultProf && !this.selectedScoringProfileId) {
          this.selectedScoringProfileId = defaultProf.id;
        } else if (this.scoringProfiles.length > 0 && !this.selectedScoringProfileId) {
          this.selectedScoringProfileId = this.scoringProfiles[0].id;
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingProfiles = false;
        console.error('Failed to load scoring profiles', err);
        this.cdr.detectChanges();
      }
    });
  }

  formatProfileOption(profile: BenchmarkScoringProfileDto): string {
    const cleanName = (profile.name || '').replace(/\s*\(Default\)$/i, '').trim();
    return profile.isDefault ? `${cleanName} (Default)` : cleanName;
  }

  openManageProfiles() {
    this.scoringProfilesDialog?.nativeElement.showModal();
  }

  closeManageProfiles() {
    this.scoringProfilesDialog?.nativeElement.close();
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
      speedTargetMs: 5000,
      speedDecayK: 25.0,
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
      speedTargetMs: profile.speedTargetMs,
      speedDecayK: profile.speedDecayK,
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

    if (this.editingProfileId) {
      this.benchmarkService.updateScoringProfile(this.editingProfileId, this.profileForm as UpdateBenchmarkScoringProfileRequest).subscribe({
        next: () => {
          this.scoringProfileFormDialog?.nativeElement.close();
          this.loadProfiles();
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
          this.loadProfiles();
          this.selectedScoringProfileId = created.id;
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
      next: () => this.loadProfiles(),
      error: (err) => console.error('Failed to set default profile', err)
    });
  }

  openConfirmDialog(options: {
    title: string;
    message: string;
    dangerNotice?: string;
    buttonText?: string;
    buttonClass?: string;
    icon?: 'delete' | 'none';
    action: () => void;
  }) {
    this.confirmDialogTitle = options.title;
    this.confirmDialogMessage = options.message;
    this.confirmDialogDangerNotice = options.dangerNotice || '';
    this.confirmDialogButtonText = options.buttonText || 'Delete';
    this.confirmDialogButtonClass = options.buttonClass || 'btn-gh btn-gh-delete';
    this.confirmDialogIcon = options.icon || 'delete';
    this.pendingConfirmAction = options.action;
    this.confirmActionDialog?.nativeElement.showModal();
  }

  closeConfirmDialog() {
    this.confirmActionDialog?.nativeElement.close();
    this.pendingConfirmAction = null;
  }

  executeConfirmAction() {
    const action = this.pendingConfirmAction;
    this.closeConfirmDialog();
    if (action) {
      action();
    }
  }

  deleteProfile(profileId: number) {
    const profile = this.scoringProfiles.find(p => p.id === profileId);
    const name = profile ? `"${profile.name}"` : 'this scoring profile';
    this.openConfirmDialog({
      title: 'Delete Scoring Profile',
      message: `Are you sure you want to delete ${name}?`,
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Profile',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteScoringProfile(profileId).subscribe({
          next: () => this.loadProfiles(),
          error: (err) => console.error('Failed to delete profile', err)
        });
      }
    });
  }

  // --- Suites Management ---

  loadSuites() {
    this.loadingSuites = true;
    this.benchmarkService.getSuites().subscribe({
      next: (data) => {
        this.suites = data;
        this.loadingSuites = false;
        if (this.suites.length > 0 && (!this.selectedSuiteId || !this.suites.some(s => s.id === this.selectedSuiteId))) {
          this.selectedSuiteId = this.suites[0].id;
        }
        this.loadAllFootprints();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingSuites = false;
        console.error('Failed to load benchmark suites', err);
        this.cdr.detectChanges();
      }
    });
  }

  loadAllFootprints() {
    for (const suite of this.suites) {
      this.benchmarkService.getSuiteRunsFootprint(suite.id).subscribe({
        next: (fp) => {
          this.footprints[suite.id] = fp;
          this.cdr.detectChanges();
        },
        error: (err) => console.error(`Failed to load footprint for suite ${suite.id}`, err)
      });
    }
  }

  openBulkDeleteDialog(suite: BenchmarkSuiteDto) {
    this.suiteForBulkDelete = suite;
    this.bulkDeleteDialog?.nativeElement.showModal();
  }

  closeBulkDeleteDialog() {
    this.suiteForBulkDelete = null;
    this.bulkDeleteDialog?.nativeElement.close();
  }

  confirmDeleteSuiteRuns() {
    if (!this.suiteForBulkDelete) return;
    const suiteId = this.suiteForBulkDelete.id;
    this.deletingSuiteRuns = true;

    this.benchmarkService.deleteSuiteRuns(suiteId).subscribe({
      next: () => {
        this.deletingSuiteRuns = false;
        this.closeBulkDeleteDialog();
        this.loadHistory();
        this.loadAllFootprints();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.deletingSuiteRuns = false;
        alert(err?.error || 'Failed to delete suite runs.');
        this.cdr.detectChanges();
      }
    });
  }

  openCreateSuite() {
    this.editingSuiteId = null;
    this.suiteForm = { name: '', description: '' };
    this.suiteDialog?.nativeElement.showModal();
  }

  openEditSuite(suite: BenchmarkSuiteDto) {
    this.editingSuiteId = suite.id;
    this.suiteForm = { name: suite.name, description: suite.description };
    this.suiteDialog?.nativeElement.showModal();
  }

  saveSuite() {
    if (!this.suiteForm.name.trim()) return;

    if (this.editingSuiteId) {
      this.benchmarkService.updateSuite(this.editingSuiteId, this.suiteForm).subscribe({
        next: () => {
          this.suiteDialog?.nativeElement.close();
          this.loadSuites();
        },
        error: (err) => console.error('Failed to update suite', err)
      });
    } else {
      this.benchmarkService.createSuite(this.suiteForm).subscribe({
        next: (created) => {
          this.suiteDialog?.nativeElement.close();
          this.loadSuites();
          this.selectedSuiteId = created.id;
        },
        error: (err) => console.error('Failed to create suite', err)
      });
    }
  }

  deleteSuite(id: number) {
    const suite = this.suites.find(s => s.id === id);
    const name = suite ? `"${suite.name}"` : 'this benchmark suite';
    this.openConfirmDialog({
      title: 'Delete Benchmark Suite',
      message: `Are you sure you want to delete ${name}?`,
      dangerNotice: 'This action is permanent and will delete the suite and all its questions.',
      buttonText: 'Delete Suite',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteSuite(id).subscribe({
          next: () => this.loadSuites(),
          error: (err) => console.error('Failed to delete suite', err)
        });
      }
    });
  }

  duplicateSuite(id: number) {
    this.benchmarkService.duplicateSuite(id).subscribe({
      next: () => this.loadSuites(),
      error: (err) => console.error('Failed to duplicate suite', err)
    });
  }

  importDefaultSuite() {
    this.benchmarkService.importDefaultSuite().subscribe({
      next: () => this.loadSuites(),
      error: (err) => console.error('Failed to import default suite', err)
    });
  }

  // --- Difficulty Assessor Dialog Actions ---

  openDifficultyAssessorDialog(suite: BenchmarkSuiteDto, question: BenchmarkQuestionDto | null = null) {
    this.suiteForDifficultyAssessment = suite;
    this.difficultyAssessmentScope = question == null ? 'suite' : 'question';
    this.questionIdForDifficultyAssessment = question?.id ?? null;
    this.isDifficultyAssessorDropdownOpen = false;
    this.difficultyAssessorConfigId = this.resolveDefaultDifficultyAssessor(question);
    this.difficultyAssessorDialog?.nativeElement.showModal();
  }

  closeDifficultyAssessorDialog() {
    this.difficultyAssessorDialog?.nativeElement.close();
    this.isDifficultyAssessorDropdownOpen = false;
  }

  resolveDefaultDifficultyAssessor(question: BenchmarkQuestionDto | null): number | null {
    if (question?.assessedDifficultyModelConfigurationId && this.benchmarkCapableConfigs.some(c => c.id === question.assessedDifficultyModelConfigurationId)) {
      return question.assessedDifficultyModelConfigurationId;
    }

    if (this.currentSuiteForQuestions?.id === this.suiteForDifficultyAssessment?.id && this.questions.length > 0) {
      const assessed = this.questions
        .filter(q => q.assessedDifficultyModelConfigurationId != null && q.assessedDifficultyAtUtc != null && this.benchmarkCapableConfigs.some(c => c.id === q.assessedDifficultyModelConfigurationId))
        .sort((a, b) => new Date(b.assessedDifficultyAtUtc!).getTime() - new Date(a.assessedDifficultyAtUtc!).getTime());
      if (assessed.length > 0 && assessed[0].assessedDifficultyModelConfigurationId != null) {
        return assessed[0].assessedDifficultyModelConfigurationId;
      }
    }

    if (this.assessorConfigId && this.benchmarkCapableConfigs.some(c => c.id === this.assessorConfigId)) {
      return this.assessorConfigId;
    }

    return this.benchmarkCapableConfigs[0]?.id ?? null;
  }

  confirmDifficultyAssessment() {
    if (!this.difficultyAssessorConfigId) return;
    const configId = this.difficultyAssessorConfigId;
    const scope = this.difficultyAssessmentScope;
    const suiteId = this.suiteForDifficultyAssessment?.id;
    const questionId = this.questionIdForDifficultyAssessment;
    this.closeDifficultyAssessorDialog();

    if (scope === 'question' && questionId != null) {
      this.rateQuestionDifficulty(questionId, configId);
    } else if (suiteId != null) {
      this.rateSuiteDifficulty(suiteId, configId);
    }
  }

  rateSuiteDifficulty(suiteId: number, assessorConfigId?: number) {
    const configId = assessorConfigId ?? this.assessorConfigId;
    if (!configId) {
      return;
    }
    this.ratingDifficulty = true;
    this.benchmarkService.rateSuiteDifficulty(suiteId, configId).subscribe({
      next: (res) => {
        this.ratingDifficulty = false;
        const idx = this.suites.findIndex(s => s.id === suiteId);
        if (idx !== -1 && res.suite) {
          this.suites[idx] = res.suite;
        } else {
          this.loadSuites();
        }
        if (this.currentSuiteForQuestions?.id === suiteId) {
          this.loadQuestions(suiteId);
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.ratingDifficulty = false;
        alert(err?.error || 'Failed to rate suite difficulty.');
        this.cdr.detectChanges();
      }
    });
  }

  rateQuestionDifficulty(questionId: number, assessorConfigId?: number) {
    const configId = assessorConfigId ?? this.assessorConfigId;
    if (!configId) {
      return;
    }
    this.ratingQuestionId = questionId;
    this.benchmarkService.rateQuestionDifficulty(questionId, configId).subscribe({
      next: (res) => {
        this.ratingQuestionId = null;
        if (this.currentSuiteForQuestions) {
          this.loadQuestions(this.currentSuiteForQuestions.id);
        }
        this.loadSuites();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.ratingQuestionId = null;
        alert(err?.error || 'Failed to rate question difficulty.');
        this.cdr.detectChanges();
      }
    });
  }

  // --- Questions Management ---

  openManageQuestions(suite: BenchmarkSuiteDto) {
    this.currentSuiteForQuestions = suite;
    this.questionsDialog?.nativeElement.showModal();
    this.loadQuestions(suite.id);
  }

  loadQuestions(suiteId: number) {
    this.loadingQuestions = true;
    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (data) => {
        this.questions = data;
        this.loadingQuestions = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingQuestions = false;
        console.error('Failed to load questions', err);
        this.cdr.detectChanges();
      }
    });
  }

  openCreateQuestion() {
    this.editingQuestionId = null;
    this.questionForm = { questionText: '', difficulty: 1, expectedPoints: '' };
    this.questionFormDialog?.nativeElement.showModal();
  }

  openEditQuestion(q: BenchmarkQuestionDto) {
    this.editingQuestionId = q.id;
    this.questionForm = {
      questionText: q.questionText,
      difficulty: typeof q.difficulty === 'number' ? q.difficulty : this.parseDifficulty(q.difficulty),
      expectedPoints: q.expectedPoints || ''
    };
    this.questionFormDialog?.nativeElement.showModal();
  }

  saveQuestion() {
    if (!this.questionForm.questionText.trim() || !this.currentSuiteForQuestions) return;

    if (this.editingQuestionId) {
      this.benchmarkService.updateQuestion(this.editingQuestionId, this.questionForm).subscribe({
        next: () => {
          this.questionFormDialog?.nativeElement.close();
          this.loadQuestions(this.currentSuiteForQuestions!.id);
          this.loadSuites();
        },
        error: (err) => console.error('Failed to update question', err)
      });
    } else {
      this.benchmarkService.createQuestion(this.currentSuiteForQuestions.id, this.questionForm).subscribe({
        next: () => {
          this.questionFormDialog?.nativeElement.close();
          this.loadQuestions(this.currentSuiteForQuestions!.id);
          this.loadSuites();
        },
        error: (err) => console.error('Failed to create question', err)
      });
    }
  }

  deleteQuestion(id: number) {
    this.openConfirmDialog({
      title: 'Delete Benchmark Question',
      message: 'Are you sure you want to delete this question?',
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Question',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteQuestion(id).subscribe({
          next: () => {
            if (this.currentSuiteForQuestions) {
              this.loadQuestions(this.currentSuiteForQuestions.id);
              this.loadSuites();
            }
          },
          error: (err) => console.error('Failed to delete question', err)
        });
      }
    });
  }

  // --- Question Drag & Drop Reordering ---

  onQuestionDragStart(event: DragEvent, index: number) {
    if (event.dataTransfer) {
      event.dataTransfer.setData('text/plain', JSON.stringify({ index }));
      event.dataTransfer.effectAllowed = 'move';
      const target = (event.target as HTMLElement).closest('.question-list-item') as HTMLElement;
      if (target) {
        setTimeout(() => target.classList.add('dragging'), 0);
      }
    }
  }

  onQuestionDragEnd(event: DragEvent) {
    const target = (event.target as HTMLElement).closest('.question-list-item') as HTMLElement;
    if (target) {
      target.classList.remove('dragging');
    }
    const items = document.querySelectorAll('.question-list-item');
    items.forEach(item => item.classList.remove('drag-over', 'drag-over-top', 'drag-over-bottom'));
  }

  onQuestionDragOver(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      const rect = targetItem.getBoundingClientRect();
      const midY = rect.top + rect.height / 2;
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
      if (event.clientY < midY) {
        targetItem.classList.add('drag-over-top');
      } else {
        targetItem.classList.add('drag-over-bottom');
      }
    }
  }

  onQuestionDragLeave(event: DragEvent) {
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }
  }

  onQuestionDrop(event: DragEvent, dropIndex: number) {
    event.preventDefault();
    const targetItem = (event.target as HTMLElement).closest('.question-list-item');
    if (targetItem) {
      targetItem.classList.remove('drag-over-top', 'drag-over-bottom');
    }

    if (event.dataTransfer && this.currentSuiteForQuestions) {
      const dataStr = event.dataTransfer.getData('text/plain');
      if (dataStr) {
        try {
          const data = JSON.parse(dataStr);
          const dragIndex = data.index;
          if (dragIndex !== undefined && dragIndex !== dropIndex) {
            const item = this.questions[dragIndex];
            this.questions.splice(dragIndex, 1);

            let insertIndex = dropIndex;
            if (targetItem) {
              const rect = targetItem.getBoundingClientRect();
              const midY = rect.top + rect.height / 2;
              if (event.clientY >= midY) {
                insertIndex++;
              }
              if (dragIndex < dropIndex && event.clientY < midY) {
                // Dragging down but dropped on top half
              } else if (dragIndex < dropIndex) {
                insertIndex--;
              }
            }

            this.questions.splice(insertIndex, 0, item);

            // Re-assign order numbers locally
            this.questions.forEach((q, idx) => q.orderIndex = idx + 1);

            const orderedIds = this.questions.map(q => q.id);
            this.benchmarkService.reorderQuestions(this.currentSuiteForQuestions.id, orderedIds).subscribe({
              next: () => this.loadQuestions(this.currentSuiteForQuestions!.id),
              error: (err) => console.error('Failed to reorder questions', err)
            });
          }
        } catch (e) {
          console.error('Failed to parse drag data', e);
        }
      }
    }
  }

  // --- Run Execution ---

  startBenchmark(acknowledgeSameProvider: boolean = false) {
    if (!this.selectedSuiteId || !this.testedConfigId || !this.assessorConfigId) return;

    this.startingRun = true;
    this.runErrorMessage = null;

    const req: StartBenchmarkRunRequest = {
      suiteId: this.selectedSuiteId,
      testedModelConfigurationId: this.testedConfigId,
      assessorModelConfigurationId: this.assessorConfigId,
      scoringProfileId: this.selectedScoringProfileId,
      acknowledgeSameProvider: acknowledgeSameProvider
    };

    this.benchmarkService.startRun(req).subscribe({
      next: (res) => {
        this.startingRun = false;
        this.sameProviderDialog?.nativeElement.close();
        this.sameProviderWarning = null;
        this.activeRunId = res.runId;
        this.startPolling(res.runId);
        this.loadHistory();
        this.loadAllFootprints();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.startingRun = false;
        if (err?.status === 409 && err.error?.sameProvider) {
          this.sameProviderWarning = err.error as SameProviderWarningDto;
          this.sameProviderDialog?.nativeElement.showModal();
        } else {
          this.runErrorMessage = err?.error || 'Failed to start benchmark run.';
        }
        this.cdr.detectChanges();
      }
    });
  }

  closeSameProviderDialog() {
    this.sameProviderDialog?.nativeElement.close();
    this.sameProviderWarning = null;
  }

  confirmSameProviderRun() {
    this.startBenchmark(true);
  }

  cancelActiveRun() {
    if (!this.activeRunId) return;
    this.benchmarkService.cancelRun(this.activeRunId).subscribe({
      next: () => {
        this.pollRunDetail(this.activeRunId!);
      },
      error: (err) => console.error('Failed to cancel run', err)
    });
  }

  private startPolling(runId: number) {
    this.stopPolling();
    this.pollRunDetail(runId);
    this.pollInterval = setInterval(() => {
      this.pollRunDetail(runId);
    }, 2000);
  }

  private stopPolling() {
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
  }

  private pollRunDetail(runId: number) {
    this.benchmarkService.getRun(runId).subscribe({
      next: (run) => {
        this.activeRunDetail = run;
        const statusStr = this.formatStatus(run.status);
        if (statusStr !== 'Running') {
          this.stopPolling();
          this.loadHistory();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Failed to poll run detail', err);
        this.stopPolling();
      }
    });
  }

  // --- History & Details ---

  loadHistory() {
    this.loadingHistory = true;
    this.benchmarkService.getRuns(this.historySuiteFilter || undefined).subscribe({
      next: (data) => {
        this.historyRuns = data;
        this.loadingHistory = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingHistory = false;
        console.error('Failed to load history runs', err);
        this.cdr.detectChanges();
      }
    });
  }

  viewRunDetail(runId: number) {
    this.loadingDetail = true;
    this.selectedRunDetail = null;
    this.expandedQuestions.clear();
    this.expandedThoughts.clear();
    this.runDetailDialog?.nativeElement.showModal();

    this.benchmarkService.getRun(runId).subscribe({
      next: (data) => {
        this.selectedRunDetail = data;
        this.loadingDetail = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingDetail = false;
        console.error('Failed to load run details', err);
        this.cdr.detectChanges();
      }
    });
  }

  closeRunDetail() {
    this.selectedRunDetail = null;
    this.runDetailDialog?.nativeElement.close();
  }

  rescoreRun(runId: number) {
    this.rescoringRun = true;
    this.benchmarkService.rescoreRun(runId, this.selectedScoringProfileId).subscribe({
      next: () => {
        this.rescoringRun = false;
        this.viewRunDetail(runId);
        this.loadHistory();
      },
      error: (err) => {
        this.rescoringRun = false;
        alert(err?.error || 'Failed to rescore run.');
        this.cdr.detectChanges();
      }
    });
  }

  reassessAnswer(runId: number, answerId: number) {
    this.reassessingAnswerId = answerId;
    this.benchmarkService.reassessAnswer(runId, answerId, this.assessorConfigId).subscribe({
      next: () => {
        this.reassessingAnswerId = null;
        this.viewRunDetail(runId);
        this.loadHistory();
      },
      error: (err) => {
        this.reassessingAnswerId = null;
        alert(err?.error || 'Failed to reassess answer.');
        this.cdr.detectChanges();
      }
    });
  }

  rerunFailed(runId: number) {
    this.benchmarkService.rerunFailedQuestions(runId).subscribe({
      next: () => {
        this.activeRunId = runId;
        this.startPolling(runId);
        this.activeSubTab = 'run';
        this.closeRunDetail();
        this.loadHistory();
      },
      error: (err) => console.error('Failed to re-run failed questions', err)
    });
  }

  downloadReport(runId: number) {
    window.open(this.benchmarkService.getRunReportUrl(runId), '_blank');
  }

  deleteRun(runId: number) {
    this.openConfirmDialog({
      title: 'Delete Benchmark Run',
      message: `Are you sure you want to delete benchmark run #${runId}?`,
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Run',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.benchmarkService.deleteRun(runId).subscribe({
          next: () => {
            if (this.selectedRunDetail?.id === runId) {
              this.closeRunDetail();
            }
            this.loadHistory();
            this.loadAllFootprints();
          },
          error: (err) => console.error('Failed to delete run', err)
        });
      }
    });
  }

  toggleQuestion(orderIndex: number) {
    if (this.expandedQuestions.has(orderIndex)) {
      this.expandedQuestions.delete(orderIndex);
    } else {
      this.expandedQuestions.add(orderIndex);
    }
  }

  toggleThought(orderIndex: number) {
    if (this.expandedThoughts.has(orderIndex)) {
      this.expandedThoughts.delete(orderIndex);
    } else {
      this.expandedThoughts.add(orderIndex);
    }
  }

  // --- Formatting Helpers ---

  formatStatus(status: string | number): string {
    if (status === 1 || status === 'Running') return 'Running';
    if (status === 2 || status === 'Completed') return 'Completed';
    if (status === 3 || status === 'CompletedWithErrors') return 'CompletedWithErrors';
    if (status === 4 || status === 'Failed') return 'Failed';
    if (status === 5 || status === 'Canceled') return 'Canceled';
    return String(status);
  }

  formatAnswerStatus(status: string | number): string {
    if (status === 1 || status === 'Ok') return 'Ok';
    if (status === 2 || status === 'ProviderError') return 'ProviderError';
    if (status === 3 || status === 'Failed') return 'Failed';
    return String(status);
  }

  formatAssessmentStatus(status: string | number | undefined): string {
    if (status === 0 || status === 'Pending') return 'Pending';
    if (status === 1 || status === 'Evaluating') return 'Evaluating';
    if (status === 2 || status === 'Scored') return 'Scored';
    if (status === 3 || status === 'Failed') return 'Failed';
    if (status === 4 || status === 'Skipped') return 'Skipped';
    return status != null ? String(status) : 'Scored';
  }

  formatDifficulty(diff: string | number): string {
    if (diff === 1 || diff === 'Simple') return 'Simple';
    if (diff === 2 || diff === 'Intermediate') return 'Intermediate';
    if (diff === 3 || diff === 'Advanced') return 'Advanced';
    return String(diff);
  }

  parseDifficulty(diff: string | number): number {
    if (typeof diff === 'number') return diff;
    if (diff === 'Intermediate') return 2;
    if (diff === 'Advanced') return 3;
    return 1;
  }

  getScoreBadgeClass(score: number | null | undefined): string {
    if (score == null) return 'badge-score-na';
    if (score >= 80) return 'badge-score-high';
    if (score >= 50) return 'badge-score-mid';
    return 'badge-score-low';
  }

  getQuestionScoreBadgeClass(score: number | null | undefined): string {
    if (score == null) return 'badge-score-na';
    if (score >= 80) return 'badge-score-high';
    if (score >= 50) return 'badge-score-mid';
    return 'badge-score-low';
  }

  hasDivergence(finalScore?: number | null, computedScore?: number | null): boolean {
    if (finalScore == null || computedScore == null) return false;
    return Math.abs(finalScore - computedScore) > 10;
  }

  formatDuration(ms: number): string {
    if (!ms) return '0s';
    const totalSecs = Math.floor(ms / 1000);
    const mins = Math.floor(totalSecs / 60);
    const secs = totalSecs % 60;
    if (mins > 0) {
      return `${mins}m ${secs}s`;
    }
    return `${(ms / 1000).toFixed(1)}s`;
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return 'None';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
  }

  difficultyProgressLabel(suite: BenchmarkSuiteDto): string {
    return `Difficulty ${suite.assessedQuestionCount}/${suite.questionCount} Assessed`;
  }

  difficultyProgressClass(suite: BenchmarkSuiteDto): string {
    if (suite.difficultyFullyAssessed) return 'complete';
    if (suite.assessedQuestionCount === 0) return 'none';
    return 'partial';
  }

  get selectedSuite(): BenchmarkSuiteDto | undefined {
    return this.suites.find(s => s.id === this.selectedSuiteId);
  }

  get canStartRun(): boolean {
    return !this.startingRun &&
      !!this.selectedSuiteId &&
      !!this.testedConfigId &&
      !!this.assessorConfigId &&
      !(this.activeRunDetail && this.formatStatus(this.activeRunDetail.status) === 'Running') &&
      !!this.selectedSuite?.difficultyFullyAssessed;
  }
}
