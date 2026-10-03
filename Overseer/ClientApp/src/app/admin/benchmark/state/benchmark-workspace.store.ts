import { OnDestroy, inject, Injectable } from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkSuiteDto,
  BenchmarkQuestionDto,
  BenchmarkRunSummaryDto,
  BenchmarkScoringProfileDto,
  BenchmarkFootprintDto,
  BenchmarkRunLimitsDto,
  BenchmarkBatteryDto,
  BenchmarkRunGroupDto
} from '../../../services/admin-benchmark.service';
import { SystemAiConfigDto } from '../../../services/admin.service';
import { parseServerUtcDate } from '../../../utils/date.util';
import { TableState, anyOfFilter, customFilter } from '../../../shared/data-table/table-state';
import { CardListState } from '../../../shared/data-table/card-list-state';
import {
  ModelPickerOption,
  toModelPickerOptions
} from '../../../shared/model-picker/model-picker.component';
import { Observable, Subject, catchError, map, of } from 'rxjs';
import {
  SnapshotExport
} from '../question-yaml/question-yaml-format';
import {
  RUN_HISTORY_VIEW_STORAGE_KEY,
  RUN_HISTORY_SORTS,
  RUN_HISTORY_FLAGS,
  RUN_HISTORY_CHANGES,
  RUN_HISTORY_STARTED_RANGES,
  RUN_HISTORY_LIMIT
} from '../benchmark.models';
import { formatStatusLabel, instrumentChangeOf, runDurationMs } from '../benchmark-run-format';
import { BenchmarkViewSync } from './benchmark-view-sync.service';

/**
 * The Benchmark tab's shared data: suites, profiles, history, run groups, limits, batteries, the
 * benchmark-capable configurations and the Manage Questions list. Provided by AdminBenchmarkComponent.
 *
 * It depends on no other Benchmark state. Each loader announces a successful load on its own subject,
 * and the services that act on a fresh list (the launcher's remembered selections, the comparison's
 * pruning) subscribe to it.
 */
@Injectable()
export class BenchmarkWorkspaceStore implements OnDestroy {
  private readonly viewSync = inject(BenchmarkViewSync);
  private benchmarkService = inject(AdminBenchmarkService);

  readonly suitesLoaded$ = new Subject<void>();
  readonly profilesLoaded$ = new Subject<void>();
  /** Emits whether the battery list loaded; a failed load is announced too, as the launcher falls back on it. */
  readonly batteriesLoaded$ = new Subject<boolean>();
  readonly runLimitsLoaded$ = new Subject<void>();
  readonly historyLoaded$ = new Subject<void>();
  readonly runGroupsLoaded$ = new Subject<void>();
  /** Emits whether the Manage Questions list loaded. */
  readonly questionsLoaded$ = new Subject<boolean>();

  /** The admin page's system configurations, from AdminBenchmarkComponent's input. */
  systemConfigs: SystemAiConfigDto[] = [];

  setSystemConfigs(configs: SystemAiConfigDto[]): void {
    this.systemConfigs = configs;
  }

  /** The suite a navigation request asked to bring into view, until the next suite list arrives. */
  pendingFocusSuiteId: number | null = null;

  // Suites
  suites: BenchmarkSuiteDto[] = [];

  loadingSuites = false;

  // Scoring Profiles
  scoringProfiles: BenchmarkScoringProfileDto[] = [];

  loadingProfiles = false;

  /** Each suite's stored runs, for the suite cards and the bulk delete. */
  footprints: { [suiteId: number]: BenchmarkFootprintDto } = {};

  overseerBuildVersion: string | null = null;

  /** The caps and the live rolling-window counts. Null until GET runs/limits answers. */
  runLimits: BenchmarkRunLimitsDto | null = null;

  /** Every battery GET batteries returned; the launcher offers only `runnableBatteries`. */
  launcherBatteries: BenchmarkBatteryDto[] = [];

  loadingBatteries = false;

  // History
  historyRuns: BenchmarkRunSummaryDto[] = [];

  loadingHistory = false;

  /**
   * Sort and filter state for the Run History card list. `historyRuns` itself stays in the
   * server's own order — `instrumentChangeOf` and `completedRunsOfSelectedSuite` both locate a
   * run by its position in that list, and a user-chosen sort would make either misread the data.
   * `view()` never mutates its input, so both keep reading `historyRuns` unaffected by this.
   */
  readonly historyTable = new TableState<BenchmarkRunSummaryDto>('id', 'desc').registerAccessors(
    {
      id: r => r.id,
      suiteName: r => r.suiteName,
      testedModelDisplayNameUsed: r => r.testedModelDisplayNameUsed,
      assessorModelDisplayNameUsed: r => r.assessorModelDisplayNameUsed,
      status: r => formatStatusLabel(r.status),
      // Null sorts last automatically, which is right for a run that never scored.
      qualityIndex: r => r.qualityIndex ?? r.finalScore,
      speedIndex: r => r.speedIndex,
      // The same expression the Duration metric displays, so the list sorts by what it shows.
      durationMs: r => runDurationMs(r),
      estimatedCost: r => r.estimatedCandidateCost ?? r.estimatedCost,
      startedAtUtc: r => new Date(r.startedAtUtc)
    },
    {
      search: customFilter((r, value) => this.historySearchText(r).includes(value.toLowerCase())),
      suite: anyOfFilter(r => r.suiteName || null),
      tested: anyOfFilter(r => r.testedModelDisplayNameUsed || null),
      assessor: anyOfFilter(r => r.assessorModelDisplayNameUsed || null),
      // Keyed on the same label the status badge shows, so the facet and the card always agree.
      status: anyOfFilter(r => formatStatusLabel(r.status)),
      flags: anyOfFilter(r => this.historyFlagsOf(r)),
      changes: anyOfFilter(r => this.historyChangeOf(r)),
      started: customFilter((r, value) => this.historyStartedWithin(r, value))
    }
  );

  /** The Run History card list over `historyTable`: the search, Sort by, the facets, the chips and the batch. */
  readonly historyList = new CardListState<BenchmarkRunSummaryDto>(this.historyTable, {
    idPrefix: 'rh',
    sorts: RUN_HISTORY_SORTS,
    defaultSort: 'newest',
    storageKey: RUN_HISTORY_VIEW_STORAGE_KEY,
    facets: [
      { column: 'suite', label: 'Suite', values: r => r.suiteName || null },
      { column: 'tested', label: 'Tested model', values: r => r.testedModelDisplayNameUsed || null },
      { column: 'assessor', label: 'Assessor', values: r => r.assessorModelDisplayNameUsed || null },
      // The order of historyStatusOptions: the default string order.
      { column: 'status', label: 'Status', values: r => formatStatusLabel(r.status), order: (a, b) => (a < b ? -1 : a > b ? 1 : 0) },
      { column: 'flags', label: 'Flags', values: r => this.historyFlagsOf(r), order: RUN_HISTORY_FLAGS },
      { column: 'changes', label: 'Changes', values: r => this.historyChangeOf(r), order: RUN_HISTORY_CHANGES }
    ],
    singleFacets: [
      {
        column: 'started',
        label: 'Started',
        anyLabel: 'Any time',
        options: RUN_HISTORY_STARTED_RANGES,
        matches: (r, range) => this.historyStartedWithin(r, range),
        listedWhen: rows => rows.filter(r => this.historyStartedAt(r) !== null).length >= 2
      }
    ],
    // A debounced search applies outside any event handler, so the view is checked by hand.
    onChange: () => this.viewSync.notify()
  });

  /** What the search matches a run against, lower-cased. */
  private historySearchText(run: BenchmarkRunSummaryDto): string {
    return [
      `#${run.id}`,
      run.suiteName,
      run.testedModelDisplayNameUsed,
      run.testedModelIdUsed,
      run.testedModelProviderUsed,
      run.assessorModelDisplayNameUsed,
      formatStatusLabel(run.status),
      run.harnessVersion,
      run.candidateSystemPromptSha256,
      run.toolGuidesSha256,
      run.knowledgeBaseHeadSha,
      run.wikiHeadSha,
      run.sourceCodeHeadSha
    ].filter(part => !!part).join(' ').toLowerCase();
  }

  /** The Flags facet's values of a run, from its counts; `None` when it has none of them. */
  private historyFlagsOf(run: BenchmarkRunSummaryDto): string[] {
    const flags: string[] = [];
    if ((run.degradedAnswerCount ?? 0) > 0) flags.push('Degraded answers');
    if ((run.unansweredQuestionCount ?? 0) > 0) flags.push('Unanswered questions');
    if ((run.terminalFailureAnswerCount ?? 0) > 0) flags.push('Failed at the provider');
    if (run.speedMeasurementDegraded) flags.push('Advisory timing');
    if (run.pricingIncomplete) flags.push('Pricing incomplete');
    return flags.length > 0 ? flags : ['None'];
  }

  /** The Changes facet's value of a run, from `instrumentChangeOf`. */
  private historyChangeOf(run: BenchmarkRunSummaryDto): string {
    const change = this.instrumentChangeOf(run);
    return change ? (change.kind === 'options' ? 'Options changed' : 'Instrument changed') : 'No change';
  }

  private historyStartedAt(run: BenchmarkRunSummaryDto): Date | null {
    if (!run.startedAtUtc) {
      return null;
    }
    const started = parseServerUtcDate(run.startedAtUtc);
    return Number.isNaN(started.getTime()) ? null : started;
  }

  /** Whether a run started within the Started facet's range `range` of now. */
  private historyStartedWithin(run: BenchmarkRunSummaryDto, range: string): boolean {
    const hours = RUN_HISTORY_STARTED_RANGES.find(r => r.value === range)?.hours;
    const started = this.historyStartedAt(run);
    if (hours === undefined || !started) {
      return false;
    }
    return Date.now() - started.getTime() <= hours * 3600_000;
  }

  /** The analysis groups, which the Model Comparison picker offers beside single runs. */
  runGroups: BenchmarkRunGroupDto[] = [];

  loadingRunGroups = false;

  // Manage Questions: the suite it shows and that suite's questions, shared with the dialogs that read them.
  currentSuiteForQuestions: BenchmarkSuiteDto | null = null;

  questions: BenchmarkQuestionDto[] = [];

  loadingQuestions = false;

  actionErrorMessage: string | null = null;

  get benchmarkCapableConfigs(): SystemAiConfigDto[] {
    return this.systemConfigs.filter(c => (c.modelRole & 4) === 4 && c.hasApiKey && c.isEnabled);
  }

  readonly benchmarkPickerEmptyHint =
    'No system AI configs with the Benchmark role are enabled. Enable the Benchmark role in System Configs.';

  private stableCapableConfigs: SystemAiConfigDto[] = [];

  private benchmarkPickerCache: ModelPickerOption<SystemAiConfigDto>[] = [];

  /**
   * `benchmarkCapableConfigs` with an identity that changes only when its members or order do, for
   * child inputs and picker options. The admin page edits and reorders `systemConfigs` in place,
   * so the source array's identity alone is not a safe key.
   */
  get stableBenchmarkCapableConfigs(): SystemAiConfigDto[] {
    this.refreshCapableConfigs();
    return this.stableCapableConfigs;
  }

  get benchmarkPickerOptions(): ModelPickerOption<SystemAiConfigDto>[] {
    this.refreshCapableConfigs();
    return this.benchmarkPickerCache;
  }

  private refreshCapableConfigs(): void {
    const capable = this.benchmarkCapableConfigs;
    const cached = this.stableCapableConfigs;
    if (capable.length !== cached.length || capable.some((config, i) => config !== cached[i])) {
      this.stableCapableConfigs = capable;
      this.benchmarkPickerCache = toModelPickerOptions(capable);
    }
  }

  /** `instrumentChangeOf` over the loaded history. */
  instrumentChangeOf(run: BenchmarkRunSummaryDto):
    { kind: 'instrument' | 'options'; description: string; comparedToRunId: number } | null {
    return instrumentChangeOf(run, this.historyRuns);
  }

  // --- Scoring Profiles Management ---

  loadProfiles() {
    this.loadingProfiles = true;
    this.benchmarkService.getScoringProfiles().subscribe({
      next: (data) => {
        this.scoringProfiles = data;
        this.loadingProfiles = false;
        this.profilesLoaded$.next();
        this.viewSync.notify();
      },
      error: (err) => {
        this.loadingProfiles = false;
        console.error('Failed to load scoring profiles', err);
        this.viewSync.notify();
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
        // The launcher picks its suite here, before the footprints load; Manage Suites brings a
        // linked suite into view.
        this.suitesLoaded$.next();
        this.loadAllFootprints();
        this.refreshRunningGeneration();
        this.viewSync.notify();
      },
      error: (err) => {
        this.loadingSuites = false;
        console.error('Failed to load benchmark suites', err);
        this.viewSync.notify();
      }
    });
  }

  loadAllFootprints() {
    for (const suite of this.suites) {
      this.benchmarkService.getSuiteRunsFootprint(suite.id).subscribe({
        next: (fp) => {
          this.footprints[suite.id] = fp;
          this.viewSync.notify();
        },
        error: (err) => console.error(`Failed to load footprint for suite ${suite.id}`, err)
      });
    }
  }

  loadQuestions(suiteId: number) {
    this.loadingQuestions = true;
    this.benchmarkService.getQuestions(suiteId).subscribe({
      next: (data) => {
        this.questions = data;
        this.loadingQuestions = false;
        // Manage Questions opens a question Suite Health asked to edit, once the list holds it.
        this.questionsLoaded$.next(true);
        this.viewSync.notify();
      },
      error: (err) => {
        this.loadingQuestions = false;
        this.questionsLoaded$.next(false);
        console.error('Failed to load questions', err);
        this.viewSync.notify();
      }
    });
  }

  loadRunLimits(): void {
    this.benchmarkService.getRunLimits().subscribe({
      next: (limits) => {
        this.runLimits = limits;
        this.runLimitsLoaded$.next();
        this.viewSync.notify();
      },
      // A field that cannot bound itself is still usable, because the server re-checks. Blocking the
      // run because a courtesy lookup failed would be the wrong trade.
      error: (err) => console.error('Failed to load benchmark run limits', err)
    });
  }

  /**
   * Loads the launcher's battery list, and applies the remembered Run Target once it has arrived.
   * A remembered battery that is gone, archived or broken falls back to Single suite.
   */
  loadBatteries(): void {
    this.loadingBatteries = true;
    this.benchmarkService.getBatteries().subscribe({
      next: (batteries) => {
        this.launcherBatteries = batteries ?? [];
        this.loadingBatteries = false;
        this.batteriesLoaded$.next(true);
        this.viewSync.notify();
      },
      error: (err) => {
        this.loadingBatteries = false;
        console.error('Failed to load benchmark batteries', err);
        this.batteriesLoaded$.next(false);
        this.viewSync.notify();
      }
    });
  }

  // --- History ---

  /** Loads the newest runs of every suite; `afterLoad` runs once they are rendered. */
  loadHistory(afterLoad?: () => void) {
    this.loadingHistory = true;
    // The endpoint clamps to 200 regardless, so asking for exactly that loads every run it will return.
    this.benchmarkService.getRuns(undefined, RUN_HISTORY_LIMIT).subscribe({
      next: (data) => {
        this.historyRuns = data;
        this.historyList.invalidate();
        this.loadingHistory = false;
        this.historyLoaded$.next();
        this.viewSync.notify();
        afterLoad?.();
      },
      error: (err) => {
        this.loadingHistory = false;
        console.error('Failed to load history runs', err);
        this.viewSync.notify();
      }
    });
  }

  // --- Run groups ---

  loadRunGroups(): void {
    this.loadingRunGroups = true;
    this.benchmarkService.getRunGroups().subscribe({
      next: (groups) => {
        this.runGroups = groups;
        this.loadingRunGroups = false;
        this.runGroupsLoaded$.next();
        this.viewSync.notify();
      },
      error: (err) => {
        this.loadingRunGroups = false;
        console.error('Failed to load benchmark run groups', err);
        this.viewSync.notify();
      }
    });
  }

  // --- Question generation and snapshot export ---

  /** The suite a question generation job is running on, which holds its snapshot id while it runs. */
  runningGenerationSuiteId: number | null = null;

  refreshRunningGeneration(): void {
    this.benchmarkService.getActiveQuestionGeneration().subscribe({
      next: job => {
        this.runningGenerationSuiteId = job && job.status === 'Running' ? job.suiteId : null;
        this.viewSync.notify();
      },
      error: () => { /* Keeps the last known state; the server still refuses a conflicting job. */ }
    });
  }

  /** The attached snapshot for an export, in one call: its text, hash and metadata. `snapshot` is null for a suite without one, and when the fetch failed. */
  snapshotForExport(suite: BenchmarkSuiteDto): Observable<{ snapshot: SnapshotExport | null; failed: boolean }> {
    if (!suite.gameSnapshotId) {
      return of({ snapshot: null, failed: false });
    }
    return this.benchmarkService.getSnapshot(suite.gameSnapshotId, true).pipe(
      map(board => ({
        snapshot: {
          name: board.name,
          gnollhackVersion: board.sourceGnollHackVersion ?? null,
          capturedAtUtc: board.capturedAtUtc ?? null,
          notes: board.notes ?? null,
          sha256: board.sha256 ?? null,
          text: board.sanitizedText ?? '',
          snapshotFormat: board.snapshotFormatVersion ?? null
        } as SnapshotExport,
        failed: false
      })),
      catchError(() => of({ snapshot: null, failed: true }))
    );
  }

  ngOnDestroy(): void {
    this.historyList.dispose();
  }
}
