import { Component, OnInit, OnDestroy, OnChanges, SimpleChanges, Input, ChangeDetectorRef, HostListener, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkQuestionDto,
  BenchmarkRunSummaryDto,
  BenchmarkRunDetailDto,
  BenchmarkRunAnswerDto,
  BenchmarkScoringProfileDto,
  CreateBenchmarkSuiteRequest,
  UpdateBenchmarkSuiteRequest,
  CreateBenchmarkQuestionRequest,
  UpdateBenchmarkQuestionRequest,
  CreateBenchmarkScoringProfileRequest,
  UpdateBenchmarkScoringProfileRequest,
  StartBenchmarkRunRequest,
  SameProviderWarningDto,
  StartDifficultyAssessmentRequest,
  DifficultyAssessmentJobDto,
  DifficultyAssessmentJobItemDto,
  DifficultyAssessmentJobLogEntryDto,
  BenchmarkFootprintDto
} from '../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../services/admin.service';

import { CollapsibleMarkdownComponent } from '../../shared/collapsible-markdown/collapsible-markdown.component';
import { ensureOverlayPolyfills } from '../../utils/polyfills.util';
import { SystemService } from '../../services/system.service';
import { parseServerUtcDate, elapsedMsBetween } from '../../utils/date.util';

/**
 * One row of the run progress list: a suite question merged with its answer, if the run
 * has produced one yet. A question with no answer is genuinely Pending — the executor
 * writes an answer row only after the model replies, so no in-flight state is knowable
 * from the client.
 */
export interface BenchmarkRunProgressRow {
  orderIndex: number;
  questionText: string;
  /** Formatted answer status, or 'Pending' when the run has not answered this question yet. */
  status: string;
  /** Formatted assessment status, or '' when there is no answer yet. */
  assessmentStatus: string;
  errorMessage: string | null;
}

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
  @ViewChild('difficultyProgressHeading') difficultyProgressHeading?: ElementRef<HTMLElement>;
  @ViewChild('retryDialog') retryDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('runProgressDialog') runProgressDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('runProgressHeading') runProgressHeading?: ElementRef<HTMLElement>;

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
  private systemService = inject(SystemService);
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
  private static readonly RUN_POLL_INTERVAL_MS = 2000;
  private static readonly RUN_ELAPSED_TICK_MS = 1000;
  private runElapsedInterval: any = null;
  lastRunPollAtUtc: string | null = null;
  lastRunPollError: string | null = null;
  runQuestionsLoadError: string | null = null;
  overseerBuildVersion: string | null = null;

  activeRunId: number | null = null;
  activeRunDetail: BenchmarkRunDetailDto | null = null;
  private pollInterval: any = null;
  /**
   * Kept separate from visibilityChangeHandler, which belongs to difficulty polling.
   * One shared field would let whichever poller stops last detach the other's listener.
   */
  private runVisibilityChangeHandler: (() => void) | null = null;

  // Run Progress Dialog
  isRunProgressDialogOpen = false;
  runProgressQuestions: BenchmarkQuestionDto[] = [];
  /**
   * Suite the cached runProgressQuestions belong to; null means nothing is loaded. This,
   * not the array's length, is what gates the fetch — a suite that genuinely has no
   * questions would otherwise be re-fetched on every dialog open.
   */
  private runProgressQuestionsSuiteId: number | null = null;
  copiedRunDiagnostics = false;
  private copiedRunDiagnosticsTimer: ReturnType<typeof setTimeout> | null = null;

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
  detailPollInterval: any = null;
  reassessingAnswerId: number | null = null;
  rerunningAnswerId: number | null = null;
  runningSynthesis = false;
  retryingAssessments = false;

  // Retry Dialog
  retryScope: 'assessment' | 'question' | 'synthesis' | 'assessments' | null = null;
  retryRunId: number | null = null;
  retryAnswer: BenchmarkRunAnswerDto | null = null;
  retryAssessorConfigId: number | null = null;
  isRetryAssessorDropdownOpen = false;

  // Suite Dialogs
  editingSuiteId: number | null = null;
  suiteForm: CreateBenchmarkSuiteRequest = { name: '', description: '' };

  // Questions Dialog
  currentSuiteForQuestions: BenchmarkSuiteDto | null = null;
  questions: BenchmarkQuestionDto[] = [];
  loadingQuestions = false;
  // Difficulty Assessor Dialog State
  suiteForDifficultyAssessment: BenchmarkSuiteDto | null = null;
  difficultyAssessorConfigId: number | null = null;
  isDifficultyAssessorDropdownOpen = false;
  difficultyAssessmentScope: 'suite' | 'question' = 'suite';
  questionIdForDifficultyAssessment: number | null = null;

  difficultyDialogPhase: 'select' | 'progress' = 'select';
  difficultyJob: DifficultyAssessmentJobDto | null = null;
  difficultyJobStarting = false;
  terminatingDifficultyJob = false;
  private difficultyPollInterval: any = null;
  private visibilityChangeHandler: (() => void) | null = null;

  actionErrorMessage: string | null = null;
  difficultyDialogError: string | null = null;
  copiedDiagnostics = false;
  private copiedDiagnosticsTimer: ReturnType<typeof setTimeout> | null = null;

  get ratingDifficulty(): boolean {
    return this.difficultyJobIsRunning;
  }

  get ratingQuestionId(): number | null {
    if (this.difficultyJobIsRunning && this.difficultyJob?.scope === 'questions' && this.difficultyJob.items.length === 1) {
      return this.difficultyJob.items[0].questionId;
    }
    return null;
  }

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

  get difficultyJobProgressLabel(): string {
    return this.difficultyProgressLabel();
  }

  get failedDifficultyItems(): DifficultyAssessmentJobItemDto[] {
    return this.difficultyJob?.items.filter(i => i.status === 'Failed') || [];
  }

  get difficultyDiagnosticsText(): string {
    if (!this.difficultyJob) return '';
    const lines: string[] = [];
    lines.push(`Job ID: ${this.difficultyJob.id}`);
    lines.push(`Suite: ${this.difficultyJob.suiteName} (ID: ${this.difficultyJob.suiteId})`);
    lines.push(`Assessor: ${this.difficultyJob.assessorDisplayName}`);
    lines.push(`Status: ${this.difficultyJob.status}`);
    lines.push(`Model Calls: ${this.difficultyJob.totalModelCalls}`);
    lines.push(`Prompt Tokens: ${this.difficultyJob.promptTokens}, Output Tokens: ${this.difficultyJob.outputTokens}`);
    lines.push('');
    lines.push('--- LOG ---');
    for (const entry of this.difficultyJob.log) {
      lines.push(`[${entry.timestampUtc}] [${entry.severity.toUpperCase()}] ${entry.message}`);
      if (entry.rawExcerpt) {
        lines.push(`  Excerpt: ${entry.rawExcerpt}`);
      }
    }
    return lines.join('\n');
  }

  async copyDifficultyDiagnostics(): Promise<void> {
    const text = this.difficultyDiagnosticsText;
    if (!text) { return; }
    try {
      await navigator.clipboard.writeText(text);
      this.copiedDiagnostics = true;
      if (this.copiedDiagnosticsTimer) { clearTimeout(this.copiedDiagnosticsTimer); }
      this.copiedDiagnosticsTimer = setTimeout(() => {
        this.copiedDiagnostics = false;
        this.copiedDiagnosticsTimer = null;
        this.cdr.detectChanges();
      }, 2000);
    } catch {
      this.difficultyDialogError = 'Could not copy the diagnostics to the clipboard.';
    }
  }

  // Question Form Dialog
  editingQuestionId: number | null = null;
  questionForm: CreateBenchmarkQuestionRequest = { questionText: '', difficulty: 1, expectedPoints: '' };

  ngOnInit() {
    ensureOverlayPolyfills();
    this.loadSuites();
    this.loadProfiles();
    this.loadHistory();
    this.setDefaultModelSelections();
    this.checkActiveDifficultyAssessment();
    this.checkActiveRun();
  }

  checkActiveDifficultyAssessment(): void {
    this.benchmarkService.getActiveDifficultyAssessment().subscribe({
      next: (job) => {
        if (job) {
          this.difficultyJob = job;
          if (job.status === 'Running') {
            this.startDifficultyPolling(job.id);
          }
          this.cdr.detectChanges();
        }
      },
      error: (err) => console.error('Failed to check active difficulty assessment', err)
    });
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
    this.stopRunElapsedTicker();
    this.stopDetailPolling();
    this.stopDifficultyPolling();
    if (this.copiedDiagnosticsTimer) { clearTimeout(this.copiedDiagnosticsTimer); }
    if (this.copiedRunDiagnosticsTimer) { clearTimeout(this.copiedRunDiagnosticsTimer); }
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
    if (this.isRetryAssessorDropdownOpen && !target.closest('.retry-assessor-model-selector')) {
      this.isRetryAssessorDropdownOpen = false;
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

  get selectedRetryAssessorModel(): SystemAiConfigDto | undefined {
    return this.benchmarkCapableConfigs.find(c => c.id === this.retryAssessorConfigId);
  }

  get retryOriginalAssessorAvailable(): boolean {
    return this.selectedRunDetail?.assessorAvailable === true;
  }

  get retryAssessorDiffersFromRun(): boolean {
    return this.retryAssessorConfigId !== this.selectedRunDetail?.assessorModelConfigurationId;
  }

  toggleTestedModelDropdown(event: Event) {
    event.stopPropagation();
    this.isTestedModelDropdownOpen = !this.isTestedModelDropdownOpen;
    if (this.isTestedModelDropdownOpen) {
      this.isAssessorModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleAssessorModelDropdown(event: Event) {
    event.stopPropagation();
    this.isAssessorModelDropdownOpen = !this.isAssessorModelDropdownOpen;
    if (this.isAssessorModelDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleDifficultyAssessorDropdown(event: Event) {
    event.stopPropagation();
    this.isDifficultyAssessorDropdownOpen = !this.isDifficultyAssessorDropdownOpen;
    if (this.isDifficultyAssessorDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isAssessorModelDropdownOpen = false;
      this.isRetryAssessorDropdownOpen = false;
    }
  }

  toggleRetryAssessorDropdown(event: Event) {
    event.stopPropagation();
    this.isRetryAssessorDropdownOpen = !this.isRetryAssessorDropdownOpen;
    if (this.isRetryAssessorDropdownOpen) {
      this.isTestedModelDropdownOpen = false;
      this.isAssessorModelDropdownOpen = false;
      this.isDifficultyAssessorDropdownOpen = false;
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

  selectRetryAssessorModel(config: SystemAiConfigDto) {
    this.retryAssessorConfigId = config.id;
    this.isRetryAssessorDropdownOpen = false;
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
    this.actionErrorMessage = null;

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
        this.actionErrorMessage = err?.error || 'Failed to delete suite runs.';
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

  isDifficultyAssessorDialogOpen = false;

  openDifficultyAssessorDialog(suite?: BenchmarkSuiteDto | null, question: BenchmarkQuestionDto | null = null) {
    this.actionErrorMessage = null;
    this.difficultyDialogError = null;
    this.isDifficultyAssessorDialogOpen = true;

    if (this.difficultyJobIsRunning) {
      this.difficultyDialogPhase = 'progress';
    } else {
      if (suite) {
        this.suiteForDifficultyAssessment = suite;
      }
      this.difficultyAssessmentScope = question == null ? 'suite' : 'question';
      this.questionIdForDifficultyAssessment = question?.id ?? null;
      this.isDifficultyAssessorDropdownOpen = false;
      this.difficultyAssessorConfigId = this.resolveDefaultDifficultyAssessor(question);
      this.difficultyDialogPhase = 'select';
    }

    this.difficultyAssessorDialog?.nativeElement.showModal();
  }

  closeDifficultyAssessorDialog() {
    this.isDifficultyAssessorDialogOpen = false;
    this.difficultyAssessorDialog?.nativeElement.close();
    this.isDifficultyAssessorDropdownOpen = false;
    if (this.difficultyJobIsTerminal) {
      this.difficultyDialogPhase = 'select';
    }
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
    this.difficultyJobStarting = true;
    this.difficultyDialogError = null;

    const suiteId = this.suiteForDifficultyAssessment?.id || (this.difficultyJob?.suiteId ?? 0);
    const questionIds = this.difficultyAssessmentScope === 'question' && this.questionIdForDifficultyAssessment != null
      ? [this.questionIdForDifficultyAssessment]
      : null;

    this.benchmarkService.startDifficultyAssessment({
      suiteId,
      questionIds,
      assessorModelConfigurationId: this.difficultyAssessorConfigId
    }).subscribe({
      next: (res) => {
        this.difficultyJobStarting = false;
        this.difficultyDialogPhase = 'progress';
        this.isDifficultyAssessorDropdownOpen = false;
        this.startDifficultyPolling(res.jobId);
        this.cdr.detectChanges();
        this.difficultyProgressHeading?.nativeElement.focus();
      },
      error: (err) => {
        this.difficultyJobStarting = false;
        if (err.status === 409 && err.error) {
          this.difficultyJob = err.error as DifficultyAssessmentJobDto;
          this.difficultyDialogPhase = 'progress';
          this.isDifficultyAssessorDropdownOpen = false;
          this.startDifficultyPolling(this.difficultyJob.id);
          this.cdr.detectChanges();
          this.difficultyProgressHeading?.nativeElement.focus();
        } else {
          this.difficultyDialogError = err?.error || 'Failed to start difficulty assessment.';
          this.cdr.detectChanges();
        }
      }
    });
  }

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
          this.stopDifficultyPolling();
          this.loadSuites();
          if (this.currentSuiteForQuestions) {
            this.loadQuestions(this.currentSuiteForQuestions.id);
          }
        }
        this.cdr.detectChanges();
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
    this.terminatingDifficultyJob = true;
    this.benchmarkService.cancelDifficultyAssessment(this.difficultyJob.id).subscribe({
      next: () => {
        this.terminatingDifficultyJob = false;
        this.pollDifficultyJob(this.difficultyJob!.id);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.terminatingDifficultyJob = false;
        this.actionErrorMessage = err?.error || 'Failed to cancel assessment.';
        this.cdr.detectChanges();
      }
    });
  }

  assessAgain() {
    this.difficultyDialogPhase = 'select';
    this.cdr.detectChanges();
  }

  retryFailedQuestions() {
    if (!this.difficultyJob || this.failedDifficultyItems.length === 0) return;
    this.difficultyJobStarting = true;
    this.difficultyDialogError = null;

    const failedIds = this.failedDifficultyItems.map(i => i.questionId);
    this.benchmarkService.startDifficultyAssessment({
      suiteId: this.difficultyJob.suiteId,
      questionIds: failedIds,
      assessorModelConfigurationId: this.difficultyJob.assessorConfigId
    }).subscribe({
      next: (res) => {
        this.difficultyJobStarting = false;
        this.difficultyDialogPhase = 'progress';
        this.startDifficultyPolling(res.jobId);
        this.cdr.detectChanges();
        this.difficultyProgressHeading?.nativeElement.focus();
      },
      error: (err) => {
        this.difficultyJobStarting = false;
        if (err.status === 409 && err.error) {
          this.difficultyJob = err.error as DifficultyAssessmentJobDto;
          this.difficultyDialogPhase = 'progress';
          this.startDifficultyPolling(this.difficultyJob.id);
          this.cdr.detectChanges();
          this.difficultyProgressHeading?.nativeElement.focus();
        } else {
          this.difficultyDialogError = err?.error || 'Failed to retry failed questions.';
          this.cdr.detectChanges();
        }
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
        this.lastRunPollError = null;
        this.runQuestionsLoadError = null;
        this.runDiagnosticsCopyFailed = false;
        this.activeRunId = res.runId;
        this.startPolling(res.runId);
        this.loadHistory();
        this.loadAllFootprints();
        this.cdr.detectChanges();
        this.openRunProgressDialog();
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
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.pollRunDetail(runId);
    }, AdminBenchmarkComponent.RUN_POLL_INTERVAL_MS);

    if (typeof document !== 'undefined') {
      this.runVisibilityChangeHandler = () => {
        if (!document.hidden) {
          this.pollRunDetail(runId);
        }
      };
      document.addEventListener('visibilitychange', this.runVisibilityChangeHandler);
    }
  }

  private stopPolling() {
    if (this.pollInterval) {
      clearInterval(this.pollInterval);
      this.pollInterval = null;
    }
    if (this.runVisibilityChangeHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.runVisibilityChangeHandler);
      this.runVisibilityChangeHandler = null;
    }
  }

  private startRunElapsedTicker(): void {
    this.stopRunElapsedTicker();
    this.runElapsedInterval = setInterval(() => {
      if (typeof document !== 'undefined' && document.hidden) {
        return;
      }
      this.cdr.detectChanges();
    }, AdminBenchmarkComponent.RUN_ELAPSED_TICK_MS);
  }

  private stopRunElapsedTicker(): void {
    if (this.runElapsedInterval) {
      clearInterval(this.runElapsedInterval);
      this.runElapsedInterval = null;
    }
  }

  private pollRunDetail(runId: number) {
    this.benchmarkService.getRun(runId).subscribe({
      next: (run) => {
        this.lastRunPollAtUtc = new Date().toISOString();
        this.lastRunPollError = null;
        this.activeRunDetail = run;
        const statusStr = this.formatStatus(run.status);
        if (statusStr !== 'Running') {
          this.stopPolling();
          this.stopRunElapsedTicker();
          this.loadHistory();
        } else if (this.isRunProgressDialogOpen && !this.runElapsedInterval) {
          this.startRunElapsedTicker();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.lastRunPollAtUtc = new Date().toISOString();
        const httpStatus = err?.status ? ` (HTTP ${err.status})` : '';
        const msg = typeof err?.error === 'string' ? err.error : (err?.error?.message || err?.message || 'Polling failed');
        this.lastRunPollError = `${msg}${httpStatus}`;
        console.error('Failed to poll run detail', err);
        this.stopPolling();
      }
    });
  }

  // --- Run Progress Dialog ---

  /**
   * Which of the run's three sequential stages is executing. Derived entirely from the run
   * detail: the executor answers every question, then assesses every answer, then produces
   * the holistic synthesis, and each transition is visible in the DTO.
   */
  get runStage(): 'answering' | 'assessing' | 'finalizing' | 'terminal' {
    const run = this.activeRunDetail;
    if (!run) return 'answering';
    if (this.formatStatus(run.status) !== 'Running') return 'terminal';
    if (run.answers.length < run.totalQuestionCount) return 'answering';
    if (run.answers.some(a => this.isAssessmentIncomplete(a))) return 'assessing';
    return 'finalizing';
  }

  get runStageLabel(): string {
    const run = this.activeRunDetail;
    if (!run) return '';
    const total = this.runTotalQuestionCount;
    switch (this.runStage) {
      case 'answering':
        return `Stage 1 of 3 — Collecting answers. Answered ${this.runAnsweredCount} of ${total}.`;
      case 'assessing':
        return `Stage 2 of 3 — Assessing answers. Scored ${this.runScoredCount} of ${total}.`;
      case 'finalizing':
        return `Stage 3 of 3 — Synthesis and scoring. All ${total} answers assessed.`;
      default: {
        const status = this.formatStatus(run.status);
        const label = status === 'CompletedWithErrors' ? 'Completed with errors' : status;
        const failed = this.runFailedAnswerCount;
        return failed > 0
          ? `${label}. Answered ${this.runAnsweredCount} of ${total}, ${failed} failed.`
          : `${label}. Answered ${this.runAnsweredCount} of ${total}.`;
      }
    }
  }

  get runAnsweredCount(): number {
    return this.activeRunDetail?.answers.length ?? 0;
  }

  /** Answers that reached a terminal assessment state — scored or failed to assess. */
  get runScoredCount(): number {
    return (this.activeRunDetail?.answers ?? []).filter(a => {
      const s = this.formatAssessmentStatus(a.assessmentStatus);
      return s === 'Scored' || s === 'Failed';
    }).length;
  }

  get runFailedAnswerCount(): number {
    return this.runFailedAnswers.length;
  }

  get runFailedAnswers(): BenchmarkRunAnswerDto[] {
    return (this.activeRunDetail?.answers ?? []).filter(a => this.isAnswerFailed(a));
  }

  get runTotalQuestionCount(): number {
    return this.activeRunDetail?.totalQuestionCount ?? 0;
  }

  get runIsRunning(): boolean {
    return this.activeRunDetail != null && this.formatStatus(this.activeRunDetail.status) === 'Running';
  }

  get runIsTerminal(): boolean {
    return this.activeRunDetail != null && this.formatStatus(this.activeRunDetail.status) !== 'Running';
  }

  /**
   * The suite's questions merged with whatever answers the run has produced. Falls back to
   * the answers alone when the suite fetch has not landed (or failed), so the list is never
   * empty while the run is visibly progressing.
   */
  get runProgressRows(): BenchmarkRunProgressRow[] {
    const run = this.activeRunDetail;
    if (!run) return [];

    const answersByIndex = new Map<number, BenchmarkRunAnswerDto>();
    for (const a of run.answers) {
      answersByIndex.set(a.orderIndex, a);
    }

    const source = this.runProgressQuestions.length > 0
      ? this.runProgressQuestions.map(q => ({ orderIndex: q.orderIndex, questionText: q.questionText }))
      : run.answers.map(a => ({ orderIndex: a.orderIndex, questionText: a.questionText }));

    return [...source]
      .sort((a, b) => a.orderIndex - b.orderIndex)
      .map(q => {
        const ans = answersByIndex.get(q.orderIndex);
        if (!ans) {
          return {
            orderIndex: q.orderIndex,
            questionText: q.questionText,
            status: 'Pending',
            assessmentStatus: '',
            errorMessage: null
          };
        }
        return {
          orderIndex: q.orderIndex,
          questionText: q.questionText,
          status: this.formatAnswerStatus(ans.status),
          assessmentStatus: this.formatAssessmentStatus(ans.assessmentStatus),
          errorMessage: ans.errorMessage ?? null
        };
      });
  }

  /**
   * The chip's word, never a hue alone. 'Answered' rather than 'Assessing' while the
   * assessment is merely queued — claiming work that has not started would be a guess.
   */
  runRowChipLabel(row: BenchmarkRunProgressRow): string {
    if (row.status === 'Pending') return 'Pending';
    if (row.status === 'ProviderError') return 'Provider Error';
    if (row.status !== 'Ok') return row.status;
    if (row.assessmentStatus === 'Scored') return 'Scored';
    if (row.assessmentStatus === 'Failed') return 'Assessment Failed';
    if (row.assessmentStatus === 'Assessing') return 'Assessing';
    return 'Answered';
  }

  runRowChipClass(row: BenchmarkRunProgressRow): string {
    if (row.status === 'Pending') return 'status-pending';
    if (row.status === 'ProviderError') return 'status-providererror';
    if (row.status === 'Failed') return 'status-failed';
    if (row.status === 'Skipped') return 'status-skipped';
    if (row.assessmentStatus === 'Scored') return 'status-scored';
    if (row.assessmentStatus === 'Failed') return 'status-failed';
    if (row.assessmentStatus === 'Assessing') return 'status-assessing';
    return 'status-ok';
  }

  /** Recomputed each second by the elapsed ticker (and on each poll tick). */
  get runElapsedLabel(): string {
    const run = this.activeRunDetail;
    if (!run?.startedAtUtc) return '—';
    const ms = elapsedMsBetween(run.startedAtUtc, run.completedAtUtc);
    return this.formatElapsed(ms);
  }

  get runAverageAnswerDurationLabel(): string {
    const run = this.activeRunDetail;
    if (!run || run.answers.length === 0) return '—';
    return this.formatDuration(Math.round(run.totalAnswerDurationMs / run.answers.length));
  }

  /**
   * Everything an operator would paste into a bug report, assembled from the run detail.
   * Answer text, thought text, and assessor comments are deliberately excluded: they are
   * long model-generated content already reachable through the run detail dialog and the
   * Markdown report. No credential or connection string appears in the DTO.
   */
  get runDiagnosticsText(): string {
    const run = this.activeRunDetail;
    const lines: string[] = [];

    // Header
    lines.push('=== BENCHMARK RUN DIAGNOSTICS ===');
    lines.push(`Captured:         ${new Date().toISOString()}`);
    lines.push(`Overseer build:   ${this.overseerBuildVersion || 'unknown'}`);
    const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent : 'unknown';
    lines.push(`Client:           ${userAgent}`);

    let tz = 'unknown';
    let offsetStr = '';
    try {
      tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
      const offsetMin = -new Date().getTimezoneOffset();
      const sign = offsetMin >= 0 ? '+' : '-';
      const absMin = Math.abs(offsetMin);
      const h = String(Math.floor(absMin / 60)).padStart(2, '0');
      const m = String(absMin % 60).padStart(2, '0');
      offsetStr = ` (UTC${sign}${h}:${m})`;
    } catch {
      // ignore
    }
    lines.push(`Client timezone:  ${tz}${offsetStr}`);
    const pathname = typeof location !== 'undefined' ? location.pathname : '';
    lines.push(`Page:             ${pathname}`);
    lines.push('');

    if (!run) {
      lines.push('No run detail received yet.');
      lines.push('');
    } else {
      // --- RUN ---
      lines.push('--- RUN ---');
      const stageStr = this.runStage === 'terminal' ? 'terminal' : (this.runStage === 'answering' ? '1' : this.runStage === 'assessing' ? '2' : '3');
      lines.push(`Run ID: ${run.id}, Suite: ${run.suiteName} (${run.benchmarkSuiteId ?? 'n/a'}), Status: ${this.formatStatus(run.status)}, Stage: ${stageStr}, Started by: ${run.startedByUserName || 'unknown'}`);
      lines.push(`Started (raw):    ${run.startedAtUtc}`);
      const startedParsed = run.startedAtUtc ? parseServerUtcDate(run.startedAtUtc).toISOString() : 'n/a';
      lines.push(`Started (parsed): ${startedParsed}`);
      lines.push(`Completed:        ${run.completedAtUtc ?? 'n/a'}`);
      lines.push(`Elapsed:          ${this.runElapsedLabel}`);
      lines.push('');

      // --- MODELS ---
      lines.push('--- MODELS ---');
      lines.push(`Tested:   ${run.testedModelDisplayNameUsed} (${run.testedModelProviderUsed} / ${run.testedModelIdUsed})`);
      lines.push(`          thinking: ${run.testedModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.testedModelReasoningModeUsed ?? 'default'}, service tier: ${this.formatServiceTier(run.testedModelServiceTierUsed)}, max output tokens: ${run.testedModelMaxOutputTokensUsed ?? 'default'}, parallel mode: ${run.testedModelParallelExecutionModeUsed}`);
      lines.push(`Assessor: ${run.assessorModelDisplayNameUsed} (${run.assessorModelProviderUsed} / ${run.assessorModelIdUsed}), thinking: ${run.assessorModelThinkingLevelUsed ?? 'default'}, reasoning: ${run.assessorModelReasoningModeUsed ?? 'default'}, available=${run.assessorAvailable}`);
      lines.push('');

      // --- SCORING ---
      lines.push('--- SCORING ---');
      lines.push(`Profile: ${run.scoringProfileName ?? 'n/a'} (${run.scoringProfileId ?? 'n/a'}), scoring method version: ${run.scoringMethodVersion}, max parallel questions: ${run.maxParallelQuestionsUsed}`);
      lines.push('');

      // --- PROGRESS ---
      lines.push('--- PROGRESS ---');
      lines.push(`Answered ${this.runAnsweredCount} of ${this.runTotalQuestionCount}, scored ${this.runScoredCount} of ${this.runTotalQuestionCount}, failed ${this.runFailedAnswerCount}`);
      if (this.runProgressQuestions.length > 0 && this.runProgressQuestionsSuiteId != null) {
        lines.push(`Suite questions loaded: ${this.runProgressQuestions.length} for suite ${this.runProgressQuestionsSuiteId}`);
      } else {
        lines.push('Suite questions loaded: not loaded — list degraded to answers only');
      }
      lines.push('');

      // --- TOKENS ---
      lines.push('--- TOKENS ---');
      lines.push(`input: ${run.totalInputTokens}, output: ${run.totalOutputTokens}, cache read: ${run.totalCacheReadTokens}, cache creation: ${run.totalCacheCreationTokens}`);
      lines.push(`total duration: ${this.formatDuration(run.totalDurationMs)}, total answer duration: ${this.formatDuration(run.totalAnswerDurationMs)}`);
      lines.push('');

      // --- SCORES --- (only when terminal)
      if (this.runIsTerminal) {
        lines.push('--- SCORES ---');
        lines.push(`final: ${run.finalScore ?? 'n/a'}, computed: ${run.computedScore ?? 'n/a'}, quality index: ${run.qualityIndex ?? 'n/a'}, speed index: ${run.speedIndex ?? 'n/a'}`);
        lines.push('');
      }

      // --- FLAGS ---
      lines.push('--- FLAGS ---');
      const purposePresent = !!run.purposeStatementUsed;
      lines.push(`difficultyFallbackUsed=${run.difficultyFallbackUsed}, speedMeasurementDegraded=${run.speedMeasurementDegraded}, assessmentParseFailed=${run.assessmentParseFailed}, sameProviderAcknowledged=${run.sameProviderAcknowledged ?? false}, purposeStatement present=${purposePresent}`);
      lines.push('');
    }

    // --- POLLING ---
    lines.push('--- POLLING ---');
    const runPollStr = this.pollInterval ? `active every ${AdminBenchmarkComponent.RUN_POLL_INTERVAL_MS} ms` : 'stopped';
    lines.push(`Run poll: ${runPollStr}`);
    const tickerStr = this.runElapsedInterval ? `active every ${AdminBenchmarkComponent.RUN_ELAPSED_TICK_MS} ms` : 'stopped';
    lines.push(`Elapsed ticker: ${tickerStr}`);
    if (this.lastRunPollAtUtc) {
      const pollAgoSec = Math.max(0, Math.floor((Date.now() - new Date(this.lastRunPollAtUtc).getTime()) / 1000));
      lines.push(`Last poll: ${this.lastRunPollAtUtc} (${pollAgoSec}s ago)`);
    }
    if (this.lastRunPollError) {
      lines.push(`Last poll error: ${this.lastRunPollError}`);
    }
    lines.push(`Document hidden: ${typeof document !== 'undefined' ? document.hidden : false}`);
    lines.push('');

    // --- ERRORS ---
    lines.push('--- ERRORS ---');
    let hasError = false;
    if (run?.errorMessage) {
      lines.push(`Run error: ${run.errorMessage}`);
      hasError = true;
    }
    if (this.runQuestionsLoadError) {
      lines.push(`Questions fetch error: ${this.runQuestionsLoadError}`);
      hasError = true;
    }
    if (!hasError) {
      lines.push('none');
    }
    lines.push('');

    // --- QUESTIONS ---
    if (run && this.runProgressRows.length > 0) {
      lines.push('--- QUESTIONS ---');
      for (const row of this.runProgressRows) {
        const ans = run.answers.find(a => a.orderIndex === row.orderIndex);
        if (!ans) {
          lines.push(`[Q${row.orderIndex}] status=Pending`);
          continue;
        }
        const parts = [
          `status=${this.formatAnswerStatus(ans.status)}`,
          `assessment=${this.formatAssessmentStatus(ans.assessmentStatus)}`,
          `duration=${ans.durationMs}ms`
        ];
        if (ans.timeToFirstTokenMs != null) parts.push(`ttft=${ans.timeToFirstTokenMs}ms`);
        if (ans.inputTokens != null) parts.push(`in=${ans.inputTokens}`);
        if (ans.outputTokens != null) parts.push(`out=${ans.outputTokens}`);
        if (ans.cacheReadInputTokens != null) parts.push(`cacheR=${ans.cacheReadInputTokens}`);
        if (ans.cacheCreationInputTokens != null) parts.push(`cacheC=${ans.cacheCreationInputTokens}`);
        if (ans.actualServiceTierUsed) parts.push(`tier=${ans.actualServiceTierUsed}`);
        if (ans.httpStatusCode != null) parts.push(`http=${ans.httpStatusCode}`);
        if (ans.score != null) parts.push(`score=${ans.score}`);
        lines.push(`[Q${row.orderIndex}] ${parts.join(' ')}`);
        if (ans.errorMessage) {
          lines.push(`     error: ${ans.errorMessage}`);
        }
        if (ans.assessmentError) {
          lines.push(`     assessment error: ${ans.assessmentError}`);
        }
        if (ans.assessedByModelDisplayNameUsed || ans.assessedAtUtc) {
          const assessor = `${ans.assessedByModelDisplayNameUsed || 'unknown'} (${ans.assessedByModelProviderUsed || 'unknown'} / ${ans.assessedByModelIdUsed || 'unknown'})`;
          lines.push(`     assessed by: ${assessor} at ${ans.assessedAtUtc ?? 'unknown'}`);
        }
      }
    }

    return lines.join('\n');
  }

  get runDiagnosticsCopyStatus(): string {
    if (this.copiedRunDiagnostics) return 'Diagnostics copied to clipboard';
    return this.runDiagnosticsCopyFailed ? 'Could not copy the diagnostics to the clipboard.' : '';
  }

  runDiagnosticsCopyFailed = false;

  async copyRunDiagnostics(): Promise<void> {
    const text = this.runDiagnosticsText;
    if (!text) { return; }
    try {
      await navigator.clipboard.writeText(text);
      this.runDiagnosticsCopyFailed = false;
      this.copiedRunDiagnostics = true;
      if (this.copiedRunDiagnosticsTimer) { clearTimeout(this.copiedRunDiagnosticsTimer); }
      this.copiedRunDiagnosticsTimer = setTimeout(() => {
        this.copiedRunDiagnostics = false;
        this.copiedRunDiagnosticsTimer = null;
        this.cdr.detectChanges();
      }, 2000);
    } catch {
      this.copiedRunDiagnostics = false;
      this.runDiagnosticsCopyFailed = true;
      this.runErrorMessage = 'Could not copy the benchmark run diagnostics to the clipboard.';
    }
  }

  openRunProgressDialog(): void {
    this.isRunProgressDialogOpen = true;
    this.runDiagnosticsCopyFailed = false;

    if (this.overseerBuildVersion === null) {
      this.systemService.getVersion().subscribe({
        next: (version) => {
          this.overseerBuildVersion = version;
        },
        error: (err) => {
          console.warn('Failed to get Overseer build version', err);
          this.overseerBuildVersion = 'unknown';
        }
      });
    }

    if (this.runIsRunning) {
      this.startRunElapsedTicker();
    }

    // Resolved here and not in the poll handler: the suite's questions are static for the
    // life of a run, so one fetch per dialog open is one more than strictly necessary.
    const suiteId = this.activeRunDetail?.benchmarkSuiteId ?? this.selectedSuiteId;
    if (suiteId != null && this.runProgressQuestionsSuiteId !== suiteId) {
      this.loadRunProgressQuestions(suiteId);
    }

    this.runProgressDialog?.nativeElement.showModal();
    this.cdr.detectChanges();
    this.runProgressHeading?.nativeElement.focus();
  }

  closeRunProgressDialog(): void {
    this.isRunProgressDialogOpen = false;
    this.stopRunElapsedTicker();
    this.runProgressDialog?.nativeElement.close();
    this.cdr.detectChanges();
  }

  private loadRunProgressQuestions(suiteId: number): void {
    // Claimed before the request so a second open while it is in flight does not refire it.
    this.runProgressQuestionsSuiteId = suiteId;
    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (data) => {
        this.runQuestionsLoadError = null;
        this.runProgressQuestions = data;
        this.cdr.detectChanges();
      },
      error: (err) => {
        // The dialog degrades to the answers alone rather than failing to open.
        this.runProgressQuestionsSuiteId = null;
        const msg = typeof err?.error === 'string' ? err.error : (err?.error?.message || err?.message || 'Failed to load suite questions');
        this.runQuestionsLoadError = msg;
        console.error('Failed to load suite questions for run progress', err);
      }
    });
  }

  /**
   * Reattaches the banner to a run already executing when the admin page loads. The dialog
   * stays closed — opening a modal unbidden would steal focus from whatever the admin was
   * doing. The operator reopens it from the banner.
   */
  checkActiveRun(): void {
    this.benchmarkService.getActiveRun().subscribe({
      next: (res) => {
        if (res && res.runId != null) {
          this.activeRunId = res.runId;
          this.startPolling(res.runId);
          this.cdr.detectChanges();
        }
      },
      error: (err) => console.error('Failed to check active benchmark run', err)
    });
  }

  /** Terminal-state action: hand the operator over to the existing full run detail dialog. */
  viewActiveRunDetail(): void {
    const runId = this.activeRunDetail?.id ?? this.activeRunId;
    if (runId == null) return;
    this.closeRunProgressDialog();
    this.viewRunDetail(runId);
  }

  /** Re-runs the failed questions without leaving the dialog, so the retry stays watchable. */
  rerunFailedFromProgress(): void {
    const runId = this.activeRunDetail?.id ?? this.activeRunId;
    if (runId == null) return;
    this.runErrorMessage = null;
    this.benchmarkService.rerunFailedQuestions(runId).subscribe({
      next: () => {
        this.activeRunId = runId;
        this.startPolling(runId);
        this.loadHistory();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.runErrorMessage = err?.error || 'Failed to re-run failed questions.';
        this.cdr.detectChanges();
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
    this.stopDetailPolling();
    this.selectedRunDetail = null;
    this.runDetailDialog?.nativeElement.close();
  }

  startDetailPolling(runId: number) {
    this.stopDetailPolling();
    this.detailPollInterval = setInterval(() => {
      this.refreshRunDetail(runId);
    }, 2000);
  }

  stopDetailPolling() {
    if (this.detailPollInterval) {
      clearInterval(this.detailPollInterval);
      this.detailPollInterval = null;
    }
  }

  refreshRunDetail(runId: number) {
    this.benchmarkService.getRun(runId).subscribe({
      next: (data) => {
        this.selectedRunDetail = data;
        const statusStr = this.formatStatus(data.status);
        if (statusStr !== 'Running') {
          this.stopDetailPolling();
          this.reassessingAnswerId = null;
          this.rerunningAnswerId = null;
          this.runningSynthesis = false;
          this.retryingAssessments = false;
          this.loadHistory();
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        console.error('Failed to refresh run details', err);
        this.stopDetailPolling();
        this.reassessingAnswerId = null;
        this.rerunningAnswerId = null;
        this.runningSynthesis = false;
        this.retryingAssessments = false;
        this.cdr.detectChanges();
      }
    });
  }

  cancelRunById(runId: number) {
    this.benchmarkService.cancelRun(runId).subscribe({
      next: () => {
        this.refreshRunDetail(runId);
      },
      error: (err) => {
        console.error('Failed to cancel run', err);
        this.refreshRunDetail(runId);
      }
    });
  }

  openRetryDialog(scope: 'assessment' | 'question' | 'synthesis' | 'assessments', runId: number, answer?: BenchmarkRunAnswerDto) {
    this.retryScope = scope;
    this.retryRunId = runId;
    this.retryAnswer = answer ?? null;
    this.retryAssessorConfigId = this.resolveRetryAssessor();
    this.isRetryAssessorDropdownOpen = false;
    this.retryDialog?.nativeElement.showModal();
    this.cdr.detectChanges();
  }

  closeRetryDialog() {
    this.retryScope = null;
    this.retryRunId = null;
    this.retryAnswer = null;
    this.retryAssessorConfigId = null;
    this.isRetryAssessorDropdownOpen = false;
    this.retryDialog?.nativeElement.close();
    this.cdr.detectChanges();
  }

  private resolveRetryAssessor(): number | null {
    const runAssessorId = this.selectedRunDetail?.assessorModelConfigurationId;
    if (runAssessorId != null && this.benchmarkCapableConfigs.some(c => c.id === runAssessorId)) {
      return runAssessorId;
    }
    return this.benchmarkCapableConfigs[0]?.id ?? null;
  }

  confirmRetry() {
    if (!this.retryRunId || !this.retryScope) return;

    const runId = this.retryRunId;
    const scope = this.retryScope;
    const assessorId = this.retryAssessorConfigId;
    const answer = this.retryAnswer;

    this.closeRetryDialog();

    if (scope === 'assessment') {
      if (!answer) return;
      this.actionErrorMessage = null;
      this.reassessingAnswerId = answer.id;
      this.benchmarkService.reassessAnswer(runId, answer.id, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.reassessingAnswerId = null;
          this.actionErrorMessage = err?.error || 'Failed to start reassessment.';
          this.cdr.detectChanges();
        }
      });
    } else if (scope === 'question') {
      if (!answer) return;
      this.actionErrorMessage = null;
      this.rerunningAnswerId = answer.id;
      this.benchmarkService.rerunAnswer(runId, answer.id, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.rerunningAnswerId = null;
          this.actionErrorMessage = err?.error || 'Failed to start rerun.';
          this.cdr.detectChanges();
        }
      });
    } else if (scope === 'synthesis') {
      this.actionErrorMessage = null;
      this.runningSynthesis = true;
      this.benchmarkService.rerunFinalSynthesis(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.runningSynthesis = false;
          this.actionErrorMessage = err?.error || 'Failed to start final synthesis.';
          this.cdr.detectChanges();
        }
      });
    } else if (scope === 'assessments') {
      this.actionErrorMessage = null;
      this.retryingAssessments = true;
      this.benchmarkService.retryFailedAssessments(runId, assessorId).subscribe({
        next: () => {
          this.startDetailPolling(runId);
        },
        error: (err) => {
          this.retryingAssessments = false;
          this.actionErrorMessage = err?.error || 'Failed to retry failed assessments.';
          this.cdr.detectChanges();
        }
      });
    }
  }

  rescoreRun(runId: number) {
    this.actionErrorMessage = null;
    this.rescoringRun = true;
    this.benchmarkService.rescoreRun(runId, this.selectedScoringProfileId).subscribe({
      next: () => {
        this.rescoringRun = false;
        this.viewRunDetail(runId);
        this.loadHistory();
      },
      error: (err) => {
        this.rescoringRun = false;
        this.actionErrorMessage = err?.error || 'Failed to rescore run.';
        this.cdr.detectChanges();
      }
    });
  }

  reassessAnswer(runId: number, answerId: number) {
    this.actionErrorMessage = null;
    this.reassessingAnswerId = answerId;
    this.benchmarkService.reassessAnswer(runId, answerId, this.assessorConfigId).subscribe({
      next: () => {
        this.reassessingAnswerId = null;
        this.viewRunDetail(runId);
        this.loadHistory();
      },
      error: (err) => {
        this.reassessingAnswerId = null;
        this.actionErrorMessage = err?.error || 'Failed to reassess answer.';
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

  // --- Predicates ---

  isAnswerFailed(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAnswerStatus(ans.status);
    return s === 'ProviderError' || s === 'Failed' || s === 'Skipped';
  }

  isAssessmentFailed(ans: BenchmarkRunAnswerDto): boolean {
    return this.formatAssessmentStatus(ans.assessmentStatus) === 'Failed';
  }

  isAssessmentIncomplete(ans: BenchmarkRunAnswerDto): boolean {
    const s = this.formatAssessmentStatus(ans.assessmentStatus);
    return s === 'Pending' || s === 'Assessing';
  }

  hasUnscoredAssessments(): boolean {
    return (this.selectedRunDetail?.answers ?? []).some(ans => this.isAssessmentFailed(ans) || this.isAssessmentIncomplete(ans));
  }

  isRunBusy(): boolean {
    return this.formatStatus(this.selectedRunDetail?.status ?? '') === 'Running';
  }

  wasAssessedByOther(ans: BenchmarkRunAnswerDto): boolean {
    return ans.assessedByModelConfigurationId != null &&
      ans.assessedByModelConfigurationId !== this.selectedRunDetail?.assessorModelConfigurationId;
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
    if (status === 4 || status === 'Skipped') return 'Skipped';
    return String(status);
  }

  formatAssessmentStatus(status: string | number | undefined): string {
    if (status === 1 || status === 'Pending') return 'Pending';
    if (status === 2 || status === 'Assessing') return 'Assessing';
    if (status === 3 || status === 'Scored') return 'Scored';
    if (status === 4 || status === 'Failed') return 'Failed';
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

  formatElapsed(ms: number): string {
    if (!ms || ms < 0) return '0s';
    const totalSecs = Math.floor(ms / 1000);
    const hours = Math.floor(totalSecs / 3600);
    const mins = Math.floor((totalSecs % 3600) / 60);
    const secs = totalSecs % 60;
    const pad = (n: number) => (n < 10 ? '0' + n : '' + n);
    if (hours > 0) {
      return `${hours}h ${pad(mins)}m ${pad(secs)}s`;
    }
    if (mins > 0) {
      return `${mins}m ${pad(secs)}s`;
    }
    return `${secs}s`;
  }

  formatServiceTier(tier: string | null | undefined): string {
    if (!tier) return 'None';
    if (tier.toLowerCase() === 'standard_only') return 'Standard Only';
    return tier.charAt(0).toUpperCase() + tier.slice(1);
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
      return `Assessment cancelled. Rated ${rated} of ${total} questions.`;
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
