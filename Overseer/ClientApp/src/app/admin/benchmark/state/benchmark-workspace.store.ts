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
  BenchmarkBatteryRunDto,
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
import { Observable, Subject, catchError, combineLatest, map, of, take } from 'rxjs';
import {
  SnapshotExport
} from '../question-yaml/question-yaml-format';
import {
  RUN_HISTORY_VIEW_STORAGE_KEY,
  RUN_HISTORY_SORTS,
  RUN_HISTORY_FLAGS,
  RUN_HISTORY_CHANGES,
  RUN_HISTORY_STARTED_RANGES,
  RUN_HISTORY_LIMIT,
  RUN_HISTORY_MEMBERS_STORAGE_KEY,
  RUN_HISTORY_KINDS,
  BATTERY_RUN_HISTORY_LIMIT,
  HistoryItem
} from '../benchmark.models';
import { formatStatusLabel, instrumentChangeOf, runDurationMs } from '../benchmark-run-format';
import { batteryRunStatusLabel } from '../batteries/battery.models';
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

  /** The newest battery runs, which Run History lists beside single runs. */
  batteryRuns: BenchmarkBatteryRunDto[] = [];

  /** The battery run list did not load with the last history load; Run History shows single runs only. */
  batteryRunsFailed = false;

  /** Run History lists runs that are members of a battery run; off by default. */
  showBatteryMembers = readStoredShowMembers();

  private historyItemsMemo: {
    runs: readonly BenchmarkRunSummaryDto[];
    batteries: readonly BenchmarkBatteryRunDto[];
    members: boolean;
    items: HistoryItem[];
    positions: Map<HistoryItem, number>;
  } | null = null;

  /**
   * Run History's cards: `historyRuns` in server order, merged with `batteryRuns` by start time,
   * newest first. Member runs are left out while `showBatteryMembers` is off. Memoized on its
   * inputs, so the card list sees one array until they change.
   */
  get historyItems(): HistoryItem[] {
    return this.historyItemsState().items;
  }

  /** Shows or hides battery member runs. Not a filter: Clear all leaves it as it is. */
  setShowBatteryMembers(show: boolean): void {
    if (this.showBatteryMembers === show) {
      return;
    }
    this.showBatteryMembers = show;
    try {
      localStorage.setItem(RUN_HISTORY_MEMBERS_STORAGE_KEY, show ? '1' : '0');
    } catch {
      // Storage unavailable; the choice lasts for this page.
    }
    this.historyList.resetBatch();
    this.historyList.invalidate();
  }

  private historyItemsState(): NonNullable<BenchmarkWorkspaceStore['historyItemsMemo']> {
    const memo = this.historyItemsMemo;
    if (memo && memo.runs === this.historyRuns && memo.batteries === this.batteryRuns &&
        memo.members === this.showBatteryMembers) {
      return memo;
    }
    const items = mergeHistoryItems(this.historyRuns, this.batteryRuns, this.showBatteryMembers);
    const positions = new Map<HistoryItem, number>();
    items.forEach((item, index) => positions.set(item, items.length - index));
    const next = { runs: this.historyRuns, batteries: this.batteryRuns, members: this.showBatteryMembers, items, positions };
    this.historyItemsMemo = next;
    return next;
  }

  /**
   * Sort and filter state for the Run History card list. `historyRuns` itself stays in the
   * server's own order — `instrumentChangeOf` and `completedRunsOfSelectedSuite` both locate a
   * run by its position in that list, and a user-chosen sort would make either misread the data.
   * `view()` never mutates its input, so both keep reading `historyRuns` unaffected by this.
   */
  readonly historyTable = new TableState<HistoryItem>('id', 'desc').registerAccessors(
    {
      // The card's position in `historyItems`, newest highest.
      id: item => this.historyItemsState().positions.get(item) ?? 0,
      suiteName: item => item.kind === 'run' ? item.run.suiteName : item.battery.batteryName,
      testedModelDisplayNameUsed: item => item.kind === 'run' ? item.run.testedModelDisplayNameUsed : item.battery.testedModelLabel,
      assessorModelDisplayNameUsed: item => item.kind === 'run' ? item.run.assessorModelDisplayNameUsed : item.battery.assessorLabel,
      status: item => this.historyStatusOf(item),
      // Null sorts last automatically, which is right for a run that never scored.
      qualityIndex: item => item.kind === 'run' ? item.run.qualityIndex ?? item.run.finalScore : item.battery.overallIndex,
      speedIndex: item => item.kind === 'run' ? item.run.speedIndex : item.battery.overallSpeedIndex,
      // The same expression the Duration metric displays, so the list sorts by what it shows.
      durationMs: item => item.kind === 'run' ? runDurationMs(item.run) : batteryRunDurationMs(item.battery),
      estimatedCost: item => item.kind === 'run' ? item.run.estimatedCandidateCost ?? item.run.estimatedCost : item.battery.totalCost,
      startedAtUtc: item => new Date(this.historyStartedText(item))
    },
    {
      search: customFilter((item, value) => this.historySearchText(item).includes(value.toLowerCase())),
      kind: anyOfFilter(item => historyKindOf(item)),
      suite: anyOfFilter(item => this.historySuitesOf(item)),
      tested: anyOfFilter(item => this.historyTestedOf(item)),
      assessor: anyOfFilter(item => this.historyAssessorOf(item)),
      // Keyed on the same label the status badge shows, so the facet and the card always agree.
      status: anyOfFilter(item => this.historyStatusOf(item)),
      flags: anyOfFilter(item => this.historyFlagsOf(item)),
      changes: anyOfFilter(item => this.historyChangeOf(item)),
      started: customFilter((item, value) => this.historyStartedWithin(item, value))
    }
  );

  /** The Run History card list over `historyTable`: the search, Sort by, the facets, the chips and the batch. */
  readonly historyList = new CardListState<HistoryItem>(this.historyTable, {
    idPrefix: 'rh',
    sorts: RUN_HISTORY_SORTS,
    defaultSort: 'newest',
    storageKey: RUN_HISTORY_VIEW_STORAGE_KEY,
    facets: [
      { column: 'kind', label: 'Kind', values: item => historyKindOf(item), order: RUN_HISTORY_KINDS },
      { column: 'suite', label: 'Suite', values: item => this.historySuitesOf(item) },
      { column: 'tested', label: 'Tested model', values: item => this.historyTestedOf(item) },
      { column: 'assessor', label: 'Assessor', values: item => this.historyAssessorOf(item) },
      // The order of historyStatusOptions: the default string order.
      { column: 'status', label: 'Status', values: item => this.historyStatusOf(item), order: (a, b) => (a < b ? -1 : a > b ? 1 : 0) },
      { column: 'flags', label: 'Flags', values: item => this.historyFlagsOf(item), order: RUN_HISTORY_FLAGS },
      { column: 'changes', label: 'Changes', values: item => this.historyChangeOf(item), order: RUN_HISTORY_CHANGES }
    ],
    singleFacets: [
      {
        column: 'started',
        label: 'Started',
        anyLabel: 'Any time',
        options: RUN_HISTORY_STARTED_RANGES,
        matches: (item, range) => this.historyStartedWithin(item, range),
        listedWhen: rows => rows.filter(item => this.historyStartedAt(item) !== null).length >= 2
      }
    ],
    // A debounced search applies outside any event handler, so the view is checked by hand.
    onChange: () => this.viewSync.notify()
  });

  /** What the search matches a card against, lower-cased. */
  private historySearchText(item: HistoryItem): string {
    if (item.kind === 'battery') {
      const battery = item.battery;
      return [
        `battery #${battery.id}`,
        `#${battery.id}`,
        battery.batteryName,
        ...battery.suites.map(suite => suite.suiteName),
        battery.testedModelLabel,
        battery.testedModelId,
        battery.testedProvider,
        battery.assessorLabel,
        batteryRunStatusLabel(battery.status),
        battery.definitionSha256
      ].filter(part => !!part).join(' ').toLowerCase();
    }
    const run = item.run;
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

  private historySuitesOf(item: HistoryItem): string | string[] | null {
    if (item.kind === 'run') {
      return item.run.suiteName || null;
    }
    const names = item.battery.suites.map(suite => suite.suiteName).filter(name => !!name);
    return names.length > 0 ? names : null;
  }

  private historyTestedOf(item: HistoryItem): string | null {
    return (item.kind === 'run' ? item.run.testedModelDisplayNameUsed : item.battery.testedModelLabel) || null;
  }

  private historyAssessorOf(item: HistoryItem): string | null {
    return (item.kind === 'run' ? item.run.assessorModelDisplayNameUsed : item.battery.assessorLabel) || null;
  }

  private historyStatusOf(item: HistoryItem): string {
    return item.kind === 'run' ? formatStatusLabel(item.run.status) : batteryRunStatusLabel(item.battery.status);
  }

  /** The Flags facet's values of a card; `None` when it has none of them. */
  private historyFlagsOf(item: HistoryItem): string[] {
    const flags: string[] = [];
    if (item.kind === 'battery') {
      const battery = item.battery;
      if (battery.latestAnalysisId == null) {
        flags.push('Not analyzed');
      } else {
        if (battery.analysisStale) flags.push('Analysis stale');
        if (battery.latestAnalysisComplete === false) flags.push('Incomplete');
      }
      return flags.length > 0 ? flags : ['None'];
    }
    const run = item.run;
    if ((run.degradedAnswerCount ?? 0) > 0) flags.push('Degraded answers');
    if ((run.unansweredQuestionCount ?? 0) > 0) flags.push('Unanswered questions');
    if ((run.terminalFailureAnswerCount ?? 0) > 0) flags.push('Failed at the provider');
    if (run.speedMeasurementDegraded) flags.push('Advisory timing');
    if (run.pricingIncomplete) flags.push('Pricing incomplete');
    return flags.length > 0 ? flags : ['None'];
  }

  /** The Changes facet's value of a run, from `instrumentChangeOf`; none for a battery run. */
  private historyChangeOf(item: HistoryItem): string | null {
    if (item.kind === 'battery') {
      return null;
    }
    const change = this.instrumentChangeOf(item.run);
    return change ? (change.kind === 'options' ? 'Options changed' : 'Instrument changed') : 'No change';
  }

  private historyStartedText(item: HistoryItem): string {
    return item.kind === 'run' ? item.run.startedAtUtc : item.battery.startedAtUtc;
  }

  private historyStartedAt(item: HistoryItem): Date | null {
    const text = this.historyStartedText(item);
    if (!text) {
      return null;
    }
    const started = parseServerUtcDate(text);
    return Number.isNaN(started.getTime()) ? null : started;
  }

  /** Whether a card started within the Started facet's range `range` of now. */
  private historyStartedWithin(item: HistoryItem, range: string): boolean {
    const hours = RUN_HISTORY_STARTED_RANGES.find(r => r.value === range)?.hours;
    const started = this.historyStartedAt(item);
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

  /**
   * Loads the newest runs of every suite and, in parallel, the newest battery runs; `afterLoad`
   * runs once they are rendered. A failed battery list leaves Run History with single runs only.
   */
  loadHistory(afterLoad?: () => void) {
    this.loadingHistory = true;
    // The endpoint clamps to RUN_HISTORY_LIMIT regardless, so asking for exactly that loads every run it will return.
    const batteries$ = this.benchmarkService.getBatteryRuns(undefined, BATTERY_RUN_HISTORY_LIMIT).pipe(
      map(list => ({ list: list ?? [], failed: false })),
      catchError(err => {
        console.error('Failed to load battery runs', err);
        return of({ list: [] as BenchmarkBatteryRunDto[], failed: true });
      })
    );
    combineLatest({ runs: this.benchmarkService.getRuns(undefined, RUN_HISTORY_LIMIT), batteries: batteries$ }).pipe(take(1)).subscribe({
      next: ({ runs: data, batteries }) => {
        this.historyRuns = data;
        this.batteryRuns = batteries.list;
        this.batteryRunsFailed = batteries.failed;
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

/** The stored *Show battery member runs* choice; off when none is stored or storage is unavailable. */
function readStoredShowMembers(): boolean {
  try {
    return localStorage.getItem(RUN_HISTORY_MEMBERS_STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

/** The Kind facet's value of a card. */
export function historyKindOf(item: HistoryItem): string {
  return item.kind === 'run' ? RUN_HISTORY_KINDS[0] : RUN_HISTORY_KINDS[1];
}

/** Completed minus started, or 0 while the battery run has not completed. */
export function batteryRunDurationMs(battery: BenchmarkBatteryRunDto): number {
  if (!battery.completedAtUtc) {
    return 0;
  }
  const elapsed = parseServerUtcDate(battery.completedAtUtc).getTime() - parseServerUtcDate(battery.startedAtUtc).getTime();
  return Number.isFinite(elapsed) ? Math.max(0, elapsed) : 0;
}

function startedMs(text: string | null | undefined): number {
  if (!text) {
    return Number.NEGATIVE_INFINITY;
  }
  const ms = parseServerUtcDate(text).getTime();
  return Number.isNaN(ms) ? Number.NEGATIVE_INFINITY : ms;
}

/**
 * `runs` in their own order, with each battery run placed before the first run that started no
 * later than it did; `batteries` arrive newest first. Runs with a `batteryRunId` are left out
 * unless `showMembers`.
 */
export function mergeHistoryItems(
  runs: readonly BenchmarkRunSummaryDto[],
  batteries: readonly BenchmarkBatteryRunDto[],
  showMembers: boolean
): HistoryItem[] {
  const sorted = batteries
    .slice()
    .sort((a, b) => startedMs(b.startedAtUtc) - startedMs(a.startedAtUtc) || b.id - a.id);
  const items: HistoryItem[] = [];
  let next = 0;
  const pushBattery = (battery: BenchmarkBatteryRunDto) => items.push({ kind: 'battery', key: `battery:${battery.id}`, battery });
  for (const run of runs) {
    if (!showMembers && run.batteryRunId != null) {
      continue;
    }
    const runStarted = startedMs(run.startedAtUtc);
    while (next < sorted.length && startedMs(sorted[next].startedAtUtc) >= runStarted) {
      pushBattery(sorted[next++]);
    }
    items.push({ kind: 'run', key: `run:${run.id}`, run });
  }
  while (next < sorted.length) {
    pushBattery(sorted[next++]);
  }
  return items;
}