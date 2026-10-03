import { Component, ChangeDetectorRef, DestroyRef, ElementRef, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkQuestionDto,
  CreateBenchmarkSuiteRequest,
  ImportBenchmarkQuestionsResultDto,
  CaptureBenchmarkSnapshotResponse
} from '../../../services/admin-benchmark.service';
import { CollapsibleMarkdownComponent } from '../../../shared/collapsible-markdown/collapsible-markdown.component';
import { MarkdownEditorComponent } from '../../../shared/markdown-editor/markdown-editor.component';
import { SuiteHealthComponent, SuiteHealthTab } from '../suite-health/suite-health.component';
import { QuestionGenerationDialogComponent } from '../question-generation/question-generation-dialog.component';
import { SuiteDescriptionGenerationDialogComponent } from '../description-generation/suite-description-generation-dialog.component';
import { Observable, firstValueFrom, forkJoin, map, of } from 'rxjs';
import { QuestionYamlImportDialogComponent } from '../question-yaml/question-yaml-import-dialog.component';
import { QuestionYamlHelpDialogComponent } from '../question-yaml/question-yaml-help-dialog.component';
import { SnapshotSuiteWizardComponent } from '../question-yaml/snapshot-suite-wizard.component';
import {
  ImportMode,
  serializeSuiteYaml,
  suiteYamlFileName
} from '../question-yaml/question-yaml-format';
import { SnapshotUploadDialogComponent } from '../snapshot-upload/snapshot-upload-dialog.component';
import { copyTextFromPromise } from '../../../utils/clipboard.util';
import { downloadTextFile } from '../../../utils/download.util';
import { COPY_STATUS_MS, SNAPSHOT_TEXT_EXPORT_FAILED } from '../benchmark.models';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import { BenchmarkLauncherState } from '../state/benchmark-launcher.state';
import { BenchmarkDifficultyJobService } from '../state/benchmark-difficulty-job.service';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { SuiteQuestionsDialogComponent } from './suite-questions-dialog.component';
import { ImportDefaultSuitesDialogComponent } from './import-default-suites-dialog.component';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** The Manage Suites sub-tab: the suite cards and the suite dialogs. */
@Component({
  selector: 'app-benchmark-suites-tab',
  standalone: true,
  imports: [
    CommonModule, DecimalPipe, FormsModule, CollapsibleMarkdownComponent, MarkdownEditorComponent, SuiteHealthComponent, QuestionGenerationDialogComponent, SuiteDescriptionGenerationDialogComponent, QuestionYamlImportDialogComponent, QuestionYamlHelpDialogComponent, SnapshotUploadDialogComponent, SnapshotSuiteWizardComponent, SuiteQuestionsDialogComponent, ImportDefaultSuitesDialogComponent
  ],
  templateUrl: './benchmark-suites-tab.component.html',
  styleUrls: ['./benchmark-suites-tab.component.scss']
})
export class BenchmarkSuitesTabComponent implements OnInit, OnDestroy {
  private readonly viewSync = inject(BenchmarkViewSync);
  readonly bridge = inject(BenchmarkShellBridge);
  readonly workspace = inject(BenchmarkWorkspaceStore);
  readonly launcher = inject(BenchmarkLauncherState);
  readonly difficulty = inject(BenchmarkDifficultyJobService);
  private cdr = inject(ChangeDetectorRef);
  private benchmarkService = inject(AdminBenchmarkService);

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    // Subscribed before the load, so whichever suite list arrives first brings a linked suite into view.
    this.workspace.suitesLoaded$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.focusLinkedSuite());
    this.workspace.loadSuites();
  }

  ngOnDestroy(): void {
    clearTimeout(this.suitesCopyStatusTimer);
  }

  private readonly destroyRef = inject(DestroyRef);

  @ViewChild(SuiteQuestionsDialogComponent) questionsDialogCmp?: SuiteQuestionsDialogComponent;

  @ViewChild(ImportDefaultSuitesDialogComponent) importDialogCmp?: ImportDefaultSuitesDialogComponent;

  /** The suite card a navigation request brought into view; outlined while this tab is shown. */
  linkedSuiteId: number | null = null;

  @ViewChild('suiteDialog') suiteDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('bulkDeleteDialog') bulkDeleteDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('suiteHealthDialog') suiteHealthDialog!: ElementRef<HTMLDialogElement>;

  @ViewChild('suiteHealthHeading') suiteHealthHeading?: ElementRef<HTMLElement>;

  @ViewChild('suiteDescriptionEditor') suiteDescriptionEditor?: MarkdownEditorComponent;

  @ViewChild(QuestionGenerationDialogComponent) generationDialog?: QuestionGenerationDialogComponent;

  @ViewChild('questionYamlImportDialog') questionYamlImportDialog?: QuestionYamlImportDialogComponent;

  @ViewChild('questionYamlHelpDialog') questionYamlHelpDialog?: QuestionYamlHelpDialogComponent;

  @ViewChild('suiteYamlHelpDialog') suiteYamlHelpDialog?: QuestionYamlHelpDialogComponent;

  @ViewChild('snapshotSuiteWizard') snapshotSuiteWizard?: SnapshotSuiteWizardComponent;

  @ViewChild('snapshotUploadDialog') snapshotUploadDialog?: SnapshotUploadDialogComponent;

  suiteHealthInitialTab: SuiteHealthTab = 'items';

  /** Result of the most recent suite-list action (import, for now), for the polite live region. */
  suiteActionAnnouncement = '';

  suiteForBulkDelete: BenchmarkSuiteDto | null = null;

  deletingSuiteRuns = false;

  // Suite Health. The suite whose full-screen dialog is open, or null. One at a time by
  // construction: there is a single dialog element for every suite card.
  suiteHealthSuiteId: number | null = null;

  // Suite Dialogs
  editingSuiteId: number | null = null;

  suiteForm: CreateBenchmarkSuiteRequest = { name: '', description: '' };

  private suiteFormBaseline: { name: string; description: string } = { name: '', description: '' };

  descriptionGenerationVisible = false;

  descriptionGenerationSuite: BenchmarkSuiteDto | null = null;

  /** description is optional on the request DTO; the editor's value is always a string. */
  get suiteDescriptionValue(): string {
    return this.suiteForm.description ?? '';
  }

  set suiteDescriptionValue(value: string) {
    this.suiteForm.description = value;
  }

  /** Outlines, scrolls to and focuses the suite a navigation request named, once its list has arrived. */
  focusLinkedSuite(): void {
    const suiteId = this.workspace.pendingFocusSuiteId;
    if (suiteId == null) {
      return;
    }
    // Cleared first, so a second suite list response does not repeat the focus.
    this.workspace.pendingFocusSuiteId = null;
    if (!this.workspace.suites.some(s => s.id === suiteId)) {
      this.workspace.actionErrorMessage = `The linked suite (id ${suiteId}) no longer exists.`;
      this.cdr.detectChanges();
      return;
    }
    this.linkedSuiteId = suiteId;
    this.cdr.detectChanges();
    const card = document.getElementById('bm-suite-' + suiteId);
    card?.scrollIntoView({ block: 'center' });
    card?.focus({ preventScroll: true });
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
    this.workspace.actionErrorMessage = null;

    this.benchmarkService.deleteSuiteRuns(suiteId).subscribe({
      next: () => {
        this.deletingSuiteRuns = false;
        this.closeBulkDeleteDialog();
        this.workspace.loadHistory();
        this.workspace.loadAllFootprints();
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.deletingSuiteRuns = false;
        this.workspace.actionErrorMessage = err?.error || 'Failed to delete suite runs.';
        this.cdr.detectChanges();
      }
    });
  }

  openCreateSuite() {
    this.editingSuiteId = null;
    this.descriptionGenerationSuite = null;
    this.suiteForm = { name: '', description: '' };
    this.setSuiteFormBaseline();
    this.suiteDescriptionEditor?.resetToWrite();
    this.suiteDialog?.nativeElement.showModal();
  }

  openEditSuite(suite: BenchmarkSuiteDto) {
    this.editingSuiteId = suite.id;
    this.descriptionGenerationSuite = suite;
    this.suiteForm = { name: suite.name, description: suite.description };
    this.setSuiteFormBaseline();
    this.suiteDescriptionEditor?.resetToWrite();
    this.suiteDialog?.nativeElement.showModal();
  }

  private setSuiteFormBaseline() {
    this.suiteFormBaseline = { name: this.suiteForm.name, description: this.suiteForm.description ?? '' };
  }

  get suiteFormDirty(): boolean {
    return this.suiteForm.name !== this.suiteFormBaseline.name
      || (this.suiteForm.description ?? '') !== this.suiteFormBaseline.description;
  }

  requestCloseSuiteDialog() {
    if (!this.suiteFormDirty) {
      this.suiteDialog?.nativeElement.close();
      return;
    }
    this.bridge.openConfirmDialog({
      title: 'Discard unsaved changes?',
      message: `'${this.suiteForm.name || 'This suite'}' has unsaved changes to its name or description. Close without saving?`,
      buttonText: 'Discard changes',
      buttonClass: 'btn-gh btn-gh-delete',
      icon: 'none',
      action: () => this.suiteDialog?.nativeElement.close()
    });
  }

  // Escape fires cancel on a native dialog; the dialog stays open until the guard decides.
  onSuiteDialogCancel(event: Event) {
    event.preventDefault();
    this.requestCloseSuiteDialog();
  }

  openDescriptionGeneration() {
    if (!this.editingSuiteId) return;
    this.descriptionGenerationVisible = true;
  }

  onDescriptionGenerationClosed() {
    this.descriptionGenerationVisible = false;
  }

  onDescriptionGenerated(text: string) {
    this.suiteDescriptionValue = text;
    this.suiteDescriptionEditor?.resetToWrite();
    this.cdr.detectChanges();
  }

  saveSuite() {
    if (!this.suiteForm.name.trim()) return;

    if (this.editingSuiteId) {
      this.benchmarkService.updateSuite(this.editingSuiteId, this.suiteForm).subscribe({
        next: () => {
          this.suiteDialog?.nativeElement.close();
          this.workspace.loadSuites();
        },
        error: (err) => console.error('Failed to update suite', err)
      });
    } else {
      this.benchmarkService.createSuite(this.suiteForm).subscribe({
        next: (created) => {
          this.suiteDialog?.nativeElement.close();
          this.workspace.loadSuites();
          this.launcher.selectedSuiteId = created.id;
        },
        error: (err) => console.error('Failed to create suite', err)
      });
    }
  }

  deleteSuite(id: number) {
    const suite = this.workspace.suites.find(s => s.id === id);
    const name = suite ? `"${suite.name}"` : 'this benchmark suite';
    this.bridge.openConfirmDialog({
      title: 'Delete Benchmark Suite',
      message: `Are you sure you want to delete ${name}?`,
      dangerNotice: 'This action is permanent and will delete the suite and all its questions.',
      buttonText: 'Delete Suite',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        this.workspace.actionErrorMessage = null;
        this.benchmarkService.deleteSuite(id).subscribe({
          next: () => this.workspace.loadSuites(),
          error: (err) => {
            this.workspace.actionErrorMessage = typeof err?.error === 'string' && err.error
              ? err.error
              : err?.error?.message || 'Failed to delete suite.';
            this.cdr.detectChanges();
          }
        });
      }
    });
  }

  duplicateSuite(id: number) {
    this.benchmarkService.duplicateSuite(id).subscribe({
      next: () => this.workspace.loadSuites(),
      error: (err) => console.error('Failed to duplicate suite', err)
    });
  }

  // --- Import Default Suites Dialog ---

  openImportDefaultSuitesDialog(): void {
    this.importDialogCmp?.open();
  }

  /** An import finished: the suite cards reload and the live region announces what happened. */
  onDefaultSuitesImported(announcement: string): void {
    this.workspace.loadSuites();
    this.suiteActionAnnouncement = announcement;
    this.cdr.detectChanges();
  }

  // --- Questions Management ---

  openManageQuestions(suite: BenchmarkSuiteDto) {
    this.questionsDialogCmp?.open(suite);
  }

  /** A question was saved in Manage Questions: the suite cards reload, and an open generation dialog. */
  onQuestionSaved(_suiteId: number): void {
    this.workspace.loadSuites();
    if (this.generationDialogVisible) {
      this.generationDialog?.refreshQuestions();
    }
  }

  /** Opens the full-screen Suite Health dialog for one suite. */
  openSuiteHealth(suite: BenchmarkSuiteDto): void {
    this.suiteHealthSuiteId = suite.id;
    // The dialog's @if content has to exist before showModal(), or an empty dialog opens.
    this.cdr.detectChanges();
    this.suiteHealthDialog?.nativeElement.showModal();
    // showModal() would otherwise focus the close button, which announces "Close" as the
    // first thing a screen-reader user hears in a dialog full of statistics.
    this.suiteHealthHeading?.nativeElement.focus();
  }

  closeSuiteHealth(): void {
    // close() fires the dialog's (close) event, so the state is cleared in one place.
    this.suiteHealthDialog?.nativeElement.close();
  }

  /** Also reached by Escape and by platform back gestures, which bypass closeSuiteHealth(). */
  onSuiteHealthDialogClose(): void {
    this.suiteHealthSuiteId = null;
    this.cdr.detectChanges();
  }

  get suiteHealthSuite(): BenchmarkSuiteDto | undefined {
    return this.workspace.suites.find(s => s.id === this.suiteHealthSuiteId);
  }

  /**
   * The panel's only outward action. It opens the question editor and writes nothing itself —
   * every finding in that panel is advisory, and a human decides what to change.
   */
  onSuiteHealthEditQuestion(suite: BenchmarkSuiteDto, questionId: number): void {
    // Close first: openManageQuestions() calls showModal() on another dialog, and two stacked
    // modals leave the user pressing Escape twice to get back to the page.
    this.suiteHealthDialog?.nativeElement.close();
    // The list loads asynchronously, so the editor cannot be opened here: Manage Questions opens it
    // once the question this id names actually exists in memory.
    this.questionsDialogCmp?.open(suite, questionId);
  }

  // --- Question Generation State ---
  generationDialogVisible = false;

  generationSuiteForJob: BenchmarkSuiteDto | null = null;

  isGenerationRunningFor(suite: BenchmarkSuiteDto | null | undefined): boolean {
    return !!suite && this.workspace.runningGenerationSuiteId === suite.id;
  }

  openSnapshotUpload(suite: BenchmarkSuiteDto): void {
    if (this.isGenerationRunningFor(suite)) return;
    this.snapshotUploadDialog?.open(suite);
  }

  onSnapshotUploaded(res: CaptureBenchmarkSnapshotResponse): void {
    const suite = this.workspace.suites.find(s => s.id === res.suite.id);
    const replacedId = suite?.gameSnapshotId ?? null;
    if (suite) {
      suite.gameSnapshotId = res.suite.gameSnapshotId ?? res.board.id;
      suite.gameSnapshotName = res.suite.gameSnapshotName ?? res.board.name;
      suite.gameSnapshotCharCount = res.suite.gameSnapshotCharCount ?? res.board.charCount;
    }
    if (this.workspace.currentSuiteForQuestions?.id === res.suite.id) {
      this.workspace.currentSuiteForQuestions.gameSnapshotId = res.suite.gameSnapshotId ?? res.board.id;
      this.workspace.currentSuiteForQuestions.gameSnapshotName = res.suite.gameSnapshotName ?? res.board.name;
      this.workspace.currentSuiteForQuestions.gameSnapshotCharCount = res.suite.gameSnapshotCharCount ?? res.board.charCount;
    }
    if (replacedId != null) {
      this.bridge.closeSnapshotViewerFor(replacedId);
    }
    this.suiteActionAnnouncement = `Uploaded snapshot ${res.board.name} to suite ${res.suite.name}.`;
    this.workspace.loadSuites();
    this.cdr.detectChanges();
  }

  suitesCopyStatus = '';

  suitesCopyStatusTimer: ReturnType<typeof setTimeout> | undefined;

  /** A suite can be exported once it has questions or a snapshot; a questionless file is for an agent. */
  canExportSuite(suite: BenchmarkSuiteDto): boolean {
    return suite.questionCount > 0 || !!suite.gameSnapshotId;
  }

  /** The questions of a suite for an export; a suite without any needs no request. */
  private questionsForExport(suite: BenchmarkSuiteDto): Observable<BenchmarkQuestionDto[]> {
    return suite.questionCount > 0 ? this.benchmarkService.getQuestions(suite.id) : of([]);
  }

  downloadSuiteYaml(suite: BenchmarkSuiteDto): void {
    if (!this.canExportSuite(suite)) return;
    forkJoin([this.questionsForExport(suite), this.workspace.snapshotForExport(suite)]).subscribe({
      next: ([questions, exported]) => {
        downloadTextFile(suiteYamlFileName(suite.name), serializeSuiteYaml(suite, questions, exported.snapshot));
        if (exported.failed) {
          this.setSuitesCopyStatus(SNAPSHOT_TEXT_EXPORT_FAILED);
        }
      },
      error: () => this.setSuitesCopyStatus(`Could not load the questions of ${suite.name}.`)
    });
  }

  /* The clipboard write is issued inside the click, before the questions arrive. */
  async copySuiteYaml(suite: BenchmarkSuiteDto): Promise<void> {
    if (!this.canExportSuite(suite)) return;
    let snapshotFailed = false;
    const text = firstValueFrom(
      forkJoin([this.questionsForExport(suite), this.workspace.snapshotForExport(suite)]).pipe(map(([qs, exported]) => {
        snapshotFailed = exported.failed;
        return serializeSuiteYaml(suite, qs, exported.snapshot);
      })));
    const ok = await copyTextFromPromise(text);
    if (!ok) {
      this.setSuitesCopyStatus('Could not copy; use Download Suite as YAML instead.');
    } else {
      this.setSuitesCopyStatus(snapshotFailed ? `Copied suite ${suite.name} as YAML. ${SNAPSHOT_TEXT_EXPORT_FAILED}` : `Copied suite ${suite.name} as YAML`);
    }
  }

  openQuestionYamlImport(mode: ImportMode, q?: BenchmarkQuestionDto): void {
    if (mode !== 'suite' && (this.workspace.loadingQuestions || !this.workspace.currentSuiteForQuestions)) return;
    this.questionYamlImportDialog?.open(mode, q);
  }

  openQuestionYamlHelp(): void {
    this.questionYamlHelpDialog?.open();
  }

  openSuiteYamlHelp(): void {
    this.suiteYamlHelpDialog?.open();
  }

  openSnapshotSuiteWizard(): void {
    this.snapshotSuiteWizard?.open();
  }

  /** The suite help closes before the wizard opens, so the two never stack. */
  onSuiteWizardRequestedFromHelp(): void {
    this.suiteYamlHelpDialog?.close();
    this.openSnapshotSuiteWizard();
  }

  /** The current copy of a suite the wizard names, which the list reload may have replaced. */
  private freshSuite(suite: BenchmarkSuiteDto): BenchmarkSuiteDto {
    return this.workspace.suites.find(s => s.id === suite.id) ?? suite;
  }

  onWizardAssessRequested(suite: BenchmarkSuiteDto): void {
    this.bridge.openDifficultyAssessorDialog(this.freshSuite(suite));
  }

  /** The wizard applied a description; the suite cards show the new one. */
  onWizardSuiteUpdated(): void {
    this.workspace.loadSuites();
  }

  /** The help that matches the open import: the suite help for a suite import, the question help otherwise. */
  onYamlHelpRequested(): void {
    if (this.questionYamlImportDialog?.mode === 'suite') {
      this.openSuiteYamlHelp();
      return;
    }
    this.openQuestionYamlHelp();
  }

  onQuestionsImported(_result: ImportBenchmarkQuestionsResultDto): void {
    if (this.workspace.currentSuiteForQuestions) {
      this.workspace.loadQuestions(this.workspace.currentSuiteForQuestions.id);
    }
    this.workspace.loadSuites();
    if (this.generationDialogVisible) {
      this.generationDialog?.refreshQuestions();
    }
  }

  onSuiteImported(suite: BenchmarkSuiteDto): void {
    this.suiteActionAnnouncement = `Imported suite ${suite.name}.`;
    this.workspace.loadSuites();
  }

  private setSuitesCopyStatus(message: string): void {
    this.suitesCopyStatus = message;
    this.cdr.detectChanges();
    clearTimeout(this.suitesCopyStatusTimer);
    this.suitesCopyStatusTimer = setTimeout(() => {
      this.suitesCopyStatus = '';
      this.cdr.detectChanges();
    }, COPY_STATUS_MS);
  }

  openSuiteHealthForRubrics(suite: BenchmarkSuiteDto): void {
    this.suiteHealthInitialTab = 'board-facts';
    this.openSuiteHealth(suite);
  }

  confirmVerifyAll(suite: BenchmarkSuiteDto): void {
    const unreviewedCount = (suite.questionCount || 0) - (suite.reviewedQuestionCount || 0);
    this.bridge.openConfirmDialog({
      title: 'Verify All Questions',
      message: `Attest that you have read and verified all ${unreviewedCount} unreviewed questions in '${suite.name}' against the game snapshot.`,
      dangerNotice: 'This records a human review attestation in the benchmark audit manifest.',
      buttonText: 'Verify All',
      buttonClass: 'btn-gh btn-gh-primary',
      icon: 'none',
      action: () => {
        this.benchmarkService.reviewAllQuestions(suite.id).subscribe({
          next: (res) => {
            suite.reviewedQuestionCount = res.suite.reviewedQuestionCount;
            suite.hasGeneratedQuestions = res.suite.hasGeneratedQuestions;
            if (this.workspace.currentSuiteForQuestions?.id === suite.id) {
              this.workspace.loadQuestions(suite.id);
            }
            this.cdr.detectChanges();
          }
        });
      }
    });
  }

  openGenerationDialog(suite: BenchmarkSuiteDto): void {
    this.generationSuiteForJob = suite;
    this.generationDialogVisible = true;
  }

  /** The child reports whether any question changed while it was open, so Manage Questions and the suite cards only reload when there is something new to show. */
  onGenerationDialogClosed(e: { questionsChanged: boolean }): void {
    this.generationDialogVisible = false;
    if (e.questionsChanged && this.generationSuiteForJob) {
      this.workspace.loadSuites();
      if (this.workspace.currentSuiteForQuestions?.id === this.generationSuiteForJob.id) {
        this.workspace.loadQuestions(this.generationSuiteForJob.id);
      }
    }
  }

  /** Fired on every job item completion while the workspace stays open, so a long-running generation keeps the suite card and an open Manage Questions list current. */
  onGenerationQuestionsChanged(suiteId: number): void {
    this.workspace.loadSuites();
    if (this.workspace.currentSuiteForQuestions?.id === suiteId) {
      this.workspace.loadQuestions(suiteId);
    }
  }

  onGenerationEditQuestion(q: BenchmarkQuestionDto): void {
    this.questionsDialogCmp?.openEditQuestion(q);
  }
}
