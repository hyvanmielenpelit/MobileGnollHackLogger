import { OnDestroy, inject, Injectable } from '@angular/core';
import {
  AdminBenchmarkService,
  BenchmarkBatteryRunDto,
  BenchmarkRunSummaryDto,
  BenchmarkRunGroupDto,
  BenchmarkComparabilityIndexDto,
  BenchmarkModelComparisonDto,
  BenchmarkModelComparisonPricingBasis
} from '../../../services/admin-benchmark.service';
import {
  MAX_COMPARISON_SOURCES,
  ModelComparisonSelection
} from '../model-comparison/comparison-source-picker.component';
import {
  BenchmarkModelComparisonQuery,
  ComparisonSelectedSource,
  ComparisonSelectionNotice,
  selectionNotices
} from '../model-comparison/model-comparison.models';
import { Subscription } from 'rxjs';
import { BenchmarkComparisonSelection } from '../benchmark.models';
import { BenchmarkWorkspaceStore } from './benchmark-workspace.store';
import { BenchmarkViewSync } from './benchmark-view-sync.service';
import type { ComparisonWizardPreset } from './benchmark-shell-bridge.service';
import type { ComparisonIdentifiedEvent } from '../model-comparison/model-comparison.component';
import {
  LastComparisonRecord,
  buildLastComparisonRecord,
  readLastComparison,
  writeLastComparison
} from '../comparison-tab/last-comparison';

/** The most runs the comparability index is asked about; the server's own cap. */
export const MAX_COMPARABILITY_INDEX_RUNS = 1000;

/** The most analysis groups the comparability index is asked about; the server's own cap. */
export const MAX_COMPARABILITY_INDEX_GROUPS = 500;

/** The stored selection. A record written before battery results has no `batteryRunIds`. */
type StoredComparisonSelection = BenchmarkComparisonSelection & { batteryRunIds?: number[] };

/** The Model Comparison selection, its comparison and comparability index, and their persistence. */
@Injectable()
export class BenchmarkComparisonState implements OnDestroy {
  private readonly viewSync = inject(BenchmarkViewSync);
  private readonly workspace = inject(BenchmarkWorkspaceStore);
  private benchmarkService = inject(AdminBenchmarkService);

  constructor() {
    // A remembered selection is validated against each list that arrives, because a run deleted
    // since the last visit must not be sent to the compare endpoint.
    this.workspace.historyLoaded$.subscribe(() => this.pruneComparisonSelection());
    this.workspace.runGroupsLoaded$.subscribe(() => this.pruneComparisonSelection());
  }

  /**
   * Bumped when the wizard closes, since it may have written report documents. The Comparison
   * reports card lives inside the tab's @if, so each showing of the tab creates it afresh and it
   * counts the documents on init.
   */
  comparisonReportsReloadToken = 0;

  /**
   * Whether the launcher's "How the comparison works" disclosure is open; null until the tab is
   * first shown, when it is read from storage.
   */
  comparisonHowItWorksOpen: boolean | null = null;

  /**
   * The comparison last computed and numbered in this browser, for the launcher's *Last comparison*
   * card. Read from storage once per page by `restoreLastComparison`, and replaced by each new one.
   */
  lastComparison: LastComparisonRecord | null = null;

  /** Whether `lastComparison` has been read from storage on this page. */
  private lastComparisonRestored = false;

  /**
   * The report documents written from `lastComparison`, as `listComparisons` counts them; null until
   * the list has answered, when it failed, or when it does not carry that comparison.
   */
  lastComparisonDocuments: { count: number; latestAtUtc: string | null } | null = null;

  /** Guards the documents count against an out-of-order answer, as comparisonToken does. */
  private lastComparisonDocumentsToken = 0;

  // --- Model Comparison ---
  //
  // This service owns the selection and the request, and the workspace store the lists the picker offers;
  // the picker and the comparison view are both presentational. One owner is what keeps the two panels of
  // this sub-tab from disagreeing about what is selected.

  comparison: BenchmarkModelComparisonDto | null = null;

  comparisonLoading = false;

  comparisonError: string | null = null;

  comparisonRunIds: number[] = [];

  comparisonGroupIds: number[] = [];

  /** Selected battery results. Never non-empty beside runs or groups: the server refuses the mix. */
  comparisonBatteryRunIds: number[] = [];

  /** The picker's suite scope. Null offers every suite; independent of the Run Benchmark selection. */
  comparisonSuiteId: number | null = null;

  comparisonPricingBasis: BenchmarkModelComparisonPricingBasis = 'Current';

  /**
   * The wizard band's notices. Derived from the index and the selection this component owns, so
   * the picker and the band cannot disagree about what the selection costs.
   */
  get comparisonSelectionNotices(): ComparisonSelectionNotice[] {
    return selectionNotices({
      index: this.comparabilityIndex,
      indexLoading: this.comparabilityIndexLoading,
      indexError: this.comparabilityIndexError,
      runIds: this.comparisonRunIds,
      groupIds: this.comparisonGroupIds,
      batteryRunIds: this.comparisonBatteryRunIds,
      pricingBasis: this.comparisonPricingBasis
    });
  }

  /** Every selected source, battery results included. */
  get comparisonSelectedCount(): number {
    return this.comparisonRunIds.length + this.comparisonGroupIds.length + this.comparisonBatteryRunIds.length;
  }

  /**
   * Guards against an out-of-order comparison response. Compare can be clicked faster than the
   * round trip returns, and an older payload rendered over a newer selection is worse than none —
   * it charts models the operator is no longer looking at.
   */
  private comparisonToken = 0;

  /** The comparison request in flight. Unsubscribing aborts it, and the server work with it. */
  comparisonSubscription: Subscription | null = null;

  /**
   * The comparability index for the sources on offer: which of them agree on every must-match key
   * and may therefore be charted together. Read-only, and only ever a disclosure aid — the
   * comparison endpoint decides what is actually comparable.
   */
  comparabilityIndex: BenchmarkComparabilityIndexDto | null = null;

  comparabilityIndexLoading = false;

  comparabilityIndexError: string | null = null;

  /** The index's own out-of-order guard, for the same reason comparisonToken exists. */
  private comparabilityIndexToken = 0;

  // ---------------------------------------------------------------------------------------------
  // Model Comparison
  // ---------------------------------------------------------------------------------------------

  /** Completed-or-not runs inside the current suite scope. The picker decides which are selectable. */
  get comparisonRunOptions(): BenchmarkRunSummaryDto[] {
    return this.comparisonSuiteId == null
      ? this.workspace.historyRuns
      : this.workspace.historyRuns.filter(run => run.benchmarkSuiteId === this.comparisonSuiteId);
  }

  get comparisonGroupOptions(): BenchmarkRunGroupDto[] {
    return this.comparisonSuiteId == null
      ? this.workspace.runGroups
      : this.workspace.runGroups.filter(group => group.benchmarkSuiteId === this.comparisonSuiteId);
  }

  /**
   * The battery runs Run History loaded. A battery spans several suites, so the suite scope does
   * not narrow them.
   */
  get comparisonBatteryRunOptions(): BenchmarkBatteryRunDto[] {
    return this.workspace.batteryRuns;
  }

  /**
   * Every selected source as the wizard's selection band names it, runs, then groups, then
   * battery results, each in selection order.
   *
   * Read off the current option lists rather than the raw ids: an id the suite scope no longer
   * offers is skipped rather than rendered as a placeholder, because the picker has already
   * dropped it from what it shows ticked.
   */
  get comparisonSelectedSources(): ComparisonSelectedSource[] {
    const runOptions = this.comparisonRunOptions;
    const groupOptions = this.comparisonGroupOptions;
    const batteryOptions = this.comparisonBatteryRunOptions;
    const runs: ComparisonSelectedSource[] = this.comparisonRunIds
      .map(id => runOptions.find(run => run.id === id))
      .filter((run): run is BenchmarkRunSummaryDto => run != null)
      .map(run => ({
        kind: 'run',
        id: run.id,
        label: run.testedModelDisplayNameUsed,
        provider: run.testedModelProviderUsed,
        detail: `#${run.id}`
      }));
    const groups: ComparisonSelectedSource[] = this.comparisonGroupIds
      .map(id => groupOptions.find(group => group.id === id))
      .filter((group): group is BenchmarkRunGroupDto => group != null)
      .map(group => ({
        kind: 'group',
        id: group.id,
        label: group.name,
        provider: null,
        detail: group.runCount === 1 ? '1 run' : `${group.runCount} runs`
      }));
    const batteries: ComparisonSelectedSource[] = this.comparisonBatteryRunIds
      .map(id => batteryOptions.find(battery => battery.id === id))
      .filter((battery): battery is BenchmarkBatteryRunDto => battery != null)
      .map(battery => ({
        kind: 'battery',
        id: battery.id,
        label: battery.testedModelLabel?.trim() || battery.batteryName,
        provider: battery.testedProvider ?? null,
        detail: `Battery run ${battery.id}`
      }));
    return [...runs, ...groups, ...batteries];
  }

  onComparisonSelectionChange(selection: ModelComparisonSelection): void {
    // A response still in flight was asked for the previous selection.
    this.cancelComparison();
    this.comparisonRunIds = [...selection.runIds];
    this.comparisonGroupIds = [...selection.groupIds];
    this.comparisonBatteryRunIds = [...(selection.batteryRunIds ?? [])];
    this.persistComparisonSelection();
    // The payload on hand describes the previous set of sources, so it is dropped rather than left
    // beside a changed selection. It is also what the wizard reads to know Compare has not run for
    // this selection yet, which is what puts Compare back on its Next button.
    this.comparison = null;
    this.comparisonError = null;
  }

  /**
   * Drops one source from the selection band's chips, through the same path every other
   * selection change takes — persistence, the dropped comparison payload and the Compare reset
   * all happen there and nowhere else.
   */
  onComparisonRemoveSource(source: ComparisonSelectedSource): void {
    this.onComparisonSelectionChange({
      runIds: source.kind === 'run'
        ? this.comparisonRunIds.filter(id => id !== source.id)
        : [...this.comparisonRunIds],
      groupIds: source.kind === 'group'
        ? this.comparisonGroupIds.filter(id => id !== source.id)
        : [...this.comparisonGroupIds],
      batteryRunIds: source.kind === 'battery'
        ? this.comparisonBatteryRunIds.filter(id => id !== source.id)
        : [...this.comparisonBatteryRunIds]
    });
  }

  /**
   * Replaces the selection with a preset's sources, so the wizard opens on step 1 with them selected,
   * and drops the comparison on hand, which is what returns the wizard to step 1. Battery results
   * replace runs and groups (the server refuses the mix); otherwise the preset's runs and groups are
   * selected, and a suite scope that does not offer them all is cleared. Ids are de-duplicated in
   * order and capped at `MAX_COMPARISON_SOURCES`, runs first. The host calls this before opening the
   * wizard.
   */
  applyComparisonPreset(preset: ComparisonWizardPreset): void {
    const batteryRunIds = [...new Set(preset.batteryRunIds)].slice(0, MAX_COMPARISON_SOURCES);
    if (batteryRunIds.length > 0) {
      this.onComparisonSelectionChange({ runIds: [], groupIds: [], batteryRunIds });
      this.loadMissingBatteryRuns(batteryRunIds);
      this.viewSync.notify();
      return;
    }
    const runIds = [...new Set(preset.runIds ?? [])].slice(0, MAX_COMPARISON_SOURCES);
    const groupIds = [...new Set(preset.groupIds ?? [])].slice(0, MAX_COMPARISON_SOURCES - runIds.length);
    if (this.comparisonSuiteId !== null && runIds.length + groupIds.length > 0) {
      const runsInScope = new Set(this.comparisonRunOptions.map(run => run.id));
      const groupsInScope = new Set(this.comparisonGroupOptions.map(group => group.id));
      if (runIds.some(id => !runsInScope.has(id)) || groupIds.some(id => !groupsInScope.has(id))) {
        this.comparisonSuiteId = null;
        this.loadComparabilityIndex();
      }
    }
    this.onComparisonSelectionChange({ runIds, groupIds, batteryRunIds: [] });
    this.loadMissingRunsAndGroups(runIds, groupIds);
    this.viewSync.notify();
  }

  /**
   * Loads Run History when a preset's run is not loaded yet, and the run groups when one of its groups
   * is not: the picker offers what those lists hold, and a just-finished batch may postdate them.
   */
  private loadMissingRunsAndGroups(runIds: readonly number[], groupIds: readonly number[]): void {
    const runs = new Set(this.workspace.historyRuns.map(run => run.id));
    if (runIds.some(id => !runs.has(id))) {
      this.workspace.loadHistory();
    }
    const groups = new Set(this.workspace.runGroups.map(group => group.id));
    if (groupIds.some(id => !groups.has(id))) {
      this.workspace.loadRunGroups();
    }
  }

  /**
   * Replaces the selection with the sources behind a comparison's entry keys (`run:12`, `group:3`,
   * `battery:4`) and sets its pricing basis, so the wizard opens on step 1 with them selected. Keys
   * of another form are skipped, and ids are de-duplicated in order. A set that mixes battery results
   * with runs or groups keeps the runs and groups, since the server refuses the mix. A suite scope
   * that does not offer every run and group is cleared, so none of them is pruned from the selection.
   */
  applyComparisonEntries(entryKeys: readonly string[], pricingBasis: BenchmarkModelComparisonPricingBasis): void {
    const runIds: number[] = [];
    const groupIds: number[] = [];
    const batteryRunIds: number[] = [];
    for (const key of entryKeys) {
      const match = /^(run|group|battery):(\d+)$/.exec(key);
      if (!match) { continue; }
      const id = Number(match[2]);
      const ids = match[1] === 'run' ? runIds : match[1] === 'group' ? groupIds : batteryRunIds;
      if (Number.isSafeInteger(id) && !ids.includes(id)) {
        ids.push(id);
      }
    }
    const mixed = batteryRunIds.length > 0 && runIds.length + groupIds.length > 0;

    if (this.comparisonSuiteId !== null && runIds.length + groupIds.length > 0) {
      const runsInScope = new Set(this.comparisonRunOptions.map(run => run.id));
      const groupsInScope = new Set(this.comparisonGroupOptions.map(group => group.id));
      if (runIds.some(id => !runsInScope.has(id)) || groupIds.some(id => !groupsInScope.has(id))) {
        this.comparisonSuiteId = null;
        this.loadComparabilityIndex();
      }
    }

    // Set before the selection change, which persists the basis with the selection.
    this.comparisonPricingBasis = pricingBasis;
    this.onComparisonSelectionChange({ runIds, groupIds, batteryRunIds: mixed ? [] : batteryRunIds });
    if (!mixed) {
      this.loadMissingBatteryRuns(batteryRunIds);
    }
    this.viewSync.notify();
  }

  /**
   * Loads Run History when a battery run is not loaded yet: the picker's rows are the battery runs it
   * loads, which a shortcut from another tab may not have loaded.
   */
  private loadMissingBatteryRuns(batteryRunIds: readonly number[]): void {
    const loaded = new Set(this.workspace.batteryRuns.map(battery => battery.id));
    if (batteryRunIds.some(id => !loaded.has(id))) {
      this.workspace.loadHistory();
    }
  }

  /**
   * Narrows the offered sources, and drops whatever the new scope no longer offers.
   *
   * Leaving a hidden out-of-scope id selected is how a figure ends up carrying a model the picker
   * does not show. Refetches only if something survives: a request with an empty selection is
   * refused server-side anyway. Battery results are outside the scope and are left as they are.
   */
  onComparisonSuiteChange(suiteId: number | null): void {
    this.comparisonSuiteId = suiteId;
    this.loadComparabilityIndex();

    const runsInScope = new Set(this.comparisonRunOptions.map(run => run.id));
    const groupsInScope = new Set(this.comparisonGroupOptions.map(group => group.id));
    const runIds = this.comparisonRunIds.filter(id => runsInScope.has(id));
    const groupIds = this.comparisonGroupIds.filter(id => groupsInScope.has(id));
    const dropped = runIds.length !== this.comparisonRunIds.length
      || groupIds.length !== this.comparisonGroupIds.length;

    this.comparisonRunIds = runIds;
    this.comparisonGroupIds = groupIds;
    this.persistComparisonSelection();

    if (runIds.length + groupIds.length > 0) {
      this.runComparison();
    } else if (dropped) {
      // Nothing survives the new scope, so the figures on screen describe a set that is no longer
      // selected. Clearing them is more honest than leaving them beside an empty picker.
      this.comparison = null;
      this.comparisonError = null;
    }
    this.viewSync.notify();
  }

  /** Changes the cost arithmetic over an unchanged set, so it refetches at once where one exists. */
  onComparisonPricingBasisChange(basis: BenchmarkModelComparisonPricingBasis): void {
    this.comparisonPricingBasis = basis;
    this.persistComparisonSelection();
    if (this.comparisonSelectedCount > 0) {
      this.runComparison();
    }
  }

  clearComparisonSelection(): void {
    this.comparisonRunIds = [];
    this.comparisonGroupIds = [];
    this.comparisonBatteryRunIds = [];
    this.comparison = null;
    this.comparisonError = null;
    this.persistComparisonSelection();
    this.viewSync.notify();
  }

  runComparison(): void {
    if (this.comparisonSelectedCount === 0) {
      this.comparisonError = 'Select at least one run, analysis group or battery result.';
      this.viewSync.notify();
      return;
    }

    const token = ++this.comparisonToken;
    this.comparisonLoading = true;
    this.comparisonError = null;
    this.viewSync.notify();

    // Battery results travel only when selected, so a run comparison's request is unchanged by them.
    const query: BenchmarkModelComparisonQuery = this.comparisonBatteryRunIds.length > 0
      ? {
        runIds: [...this.comparisonRunIds],
        groupIds: [...this.comparisonGroupIds],
        batteryRunIds: [...this.comparisonBatteryRunIds],
        pricingBasis: this.comparisonPricingBasis
      }
      : {
        runIds: [...this.comparisonRunIds],
        groupIds: [...this.comparisonGroupIds],
        pricingBasis: this.comparisonPricingBasis
      };

    this.comparisonSubscription?.unsubscribe();
    this.comparisonSubscription = this.benchmarkService.compareModels(query).subscribe({
      next: (result) => {
        if (token !== this.comparisonToken) { return; }
        this.comparison = result;
        this.comparisonLoading = false;
        this.viewSync.notify();
      },
      error: (err) => {
        if (token !== this.comparisonToken) { return; }
        this.comparison = null;
        this.comparisonError = err?.error || 'The comparison could not be computed.';
        this.comparisonLoading = false;
        this.viewSync.notify();
      }
    });
  }

  /**
   * Abandons the comparison in flight: the request is aborted, and the selection and the payload
   * on hand are left as they are.
   */
  cancelComparison(): void {
    if (!this.comparisonLoading) {
      return;
    }
    ++this.comparisonToken;
    this.comparisonSubscription?.unsubscribe();
    this.comparisonSubscription = null;
    this.comparisonLoading = false;
    this.viewSync.notify();
  }

  private static readonly COMPARISON_LAUNCHER_STORAGE_KEY = 'overseer.benchmark.modelComparison.launcher';

  /**
   * Reads the "How the comparison works" disclosure state once per page. With no stored record it
   * opens, and records it closed, so only the first visit shows it open unless the operator leaves
   * it that way.
   */
  restoreComparisonLauncherDisclosure(): void {
    if (this.comparisonHowItWorksOpen !== null) { return; }
    let stored: string | null = null;
    try {
      stored = localStorage.getItem(BenchmarkComparisonState.COMPARISON_LAUNCHER_STORAGE_KEY);
    } catch {
      this.comparisonHowItWorksOpen = true;
      return;
    }

    if (stored === null) {
      this.comparisonHowItWorksOpen = true;
      this.persistComparisonLauncherDisclosure(false);
      return;
    }

    let open = false;
    try {
      const parsed = JSON.parse(stored) as { howItWorksOpen?: unknown } | null;
      open = parsed?.howItWorksOpen === true;
    } catch {
      // An unreadable record leaves the disclosure closed, its default.
    }
    this.comparisonHowItWorksOpen = open;
  }

  /**
   * The disclosure's native toggle. Setting [open] from the binding fires it too, so a state that
   * matches the field is the binding's own echo and is not stored.
   */
  onComparisonHowItWorksToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open === this.comparisonHowItWorksOpen) { return; }
    this.comparisonHowItWorksOpen = open;
    this.persistComparisonLauncherDisclosure(open);
  }

  private persistComparisonLauncherDisclosure(open: boolean): void {
    try {
      localStorage.setItem(
        BenchmarkComparisonState.COMPARISON_LAUNCHER_STORAGE_KEY, JSON.stringify({ howItWorksOpen: open }));
    } catch {
      // Storage throws in private-browsing modes. Forgetting a disclosure state is not worth
      // surfacing to the operator.
    }
  }

  /** Reads the last comparison from storage once per page; a record already in memory is newer. */
  restoreLastComparison(): void {
    if (this.lastComparisonRestored) { return; }
    this.lastComparisonRestored = true;
    if (this.lastComparison === null) {
      this.lastComparison = readLastComparison();
    }
  }

  /**
   * The wizard numbered a computed comparison, or its figures or name changed: it becomes the last
   * comparison, in memory and in storage. A different comparison's documents count is dropped until
   * it is fetched again.
   */
  recordLastComparison(event: ComparisonIdentifiedEvent): void {
    let record: LastComparisonRecord;
    try {
      record = buildLastComparisonRecord(event.comparison, event.identity, new Date());
    } catch {
      return;                                   // a payload of an unexpected shape leaves the card as it was
    }
    writeLastComparison(record);
    if (this.lastComparison?.id !== record.id) {
      ++this.lastComparisonDocumentsToken;
      this.lastComparisonDocuments = null;
    }
    this.lastComparison = record;
    this.lastComparisonRestored = true;
    this.viewSync.notify();
  }

  /**
   * Counts the report documents of the last comparison from the existing comparison list. Called on
   * tab entry and after the wizard closes. A failure, or a list without that comparison, leaves the
   * count null, and the card shows no documents fact.
   */
  refreshLastComparisonDocuments(): void {
    const token = ++this.lastComparisonDocumentsToken;
    const record = this.lastComparison;
    if (!record) {
      this.lastComparisonDocuments = null;
      return;
    }
    const fail = (): void => {
      if (token !== this.lastComparisonDocumentsToken) { return; }
      this.lastComparisonDocuments = null;
      this.viewSync.notify();
    };
    try {
      this.benchmarkService.listComparisons().subscribe({
        next: rows => {
          if (token !== this.lastComparisonDocumentsToken) { return; }
          const row = Array.isArray(rows) ? rows.find(item => item.id === record.id) : undefined;
          this.lastComparisonDocuments = row && Number.isFinite(row.documentCount)
            ? { count: row.documentCount, latestAtUtc: row.lastDocumentAtUtc ?? null }
            : null;
          this.viewSync.notify();
        },
        error: fail
      });
    } catch {
      fail();
    }
  }

  /**
   * Loads the comparability index for the runs and groups currently on offer.
   *
   * A failure is non-fatal: the Condition column falls back to a dash and Compare still works. The
   * index is a disclosure aid, and a picker made unusable because an aid failed is worse than one
   * that discloses less. Battery results are not part of the index.
   */
  loadComparabilityIndex(): void {
    const runIds = this.comparisonRunOptions.slice(0, MAX_COMPARABILITY_INDEX_RUNS).map(run => run.id);
    const groupIds = this.comparisonGroupOptions.slice(0, MAX_COMPARABILITY_INDEX_GROUPS).map(group => group.id);
    if (runIds.length + groupIds.length === 0) {
      this.comparabilityIndex = null;
      this.comparabilityIndexLoading = false;
      this.comparabilityIndexError = null;
      return;
    }

    const token = ++this.comparabilityIndexToken;
    this.comparabilityIndexLoading = true;
    this.comparabilityIndexError = null;

    this.benchmarkService.getComparabilityIndex({ runIds, groupIds }).subscribe({
      next: (result) => {
        // A slow response for a suite scope the operator has already left must not overwrite a
        // newer one, exactly as with the comparison itself.
        if (token !== this.comparabilityIndexToken) { return; }
        this.comparabilityIndex = result;
        this.comparabilityIndexLoading = false;
        this.viewSync.notify();
      },
      error: (err) => {
        if (token !== this.comparabilityIndexToken) { return; }
        this.comparabilityIndex = null;
        this.comparabilityIndexLoading = false;
        this.comparabilityIndexError =
          err?.error || 'The comparability index could not be loaded, so the Condition column is ' +
          'unavailable. The comparison itself is unaffected.';
        this.viewSync.notify();
      }
    });
  }

  private static readonly COMPARISON_SELECTION_STORAGE_KEY =
    'overseer_admin_benchmark_comparison_selection';

  private persistComparisonSelection(): void {
    try {
      const selection: StoredComparisonSelection = {
        runIds: this.comparisonRunIds,
        groupIds: this.comparisonGroupIds,
        batteryRunIds: this.comparisonBatteryRunIds,
        suiteId: this.comparisonSuiteId,
        pricingBasis: this.comparisonPricingBasis
      };
      localStorage.setItem(
        BenchmarkComparisonState.COMPARISON_SELECTION_STORAGE_KEY, JSON.stringify(selection));
    } catch {
      // Storage throws in private-browsing modes. Failing to remember a selection is not worth
      // surfacing to the operator.
    }
  }

  /**
   * Restores the remembered selection, dropping every id the loaded lists no longer carry.
   *
   * Validated rather than trusted: a run deleted since the last visit would otherwise be sent, and
   * the server would answer `Run(s) not found` for a selection the operator never made. The lists
   * arrive asynchronously, so this runs again on each entry to the tab as they land.
   */
  restoreComparisonSelection(): void {
    let parsed: unknown;
    try {
      const stored = localStorage.getItem(BenchmarkComparisonState.COMPARISON_SELECTION_STORAGE_KEY);
      if (!stored) { return; }
      parsed = JSON.parse(stored);
    } catch {
      return;                                   // every default stands
    }

    const raw = parsed as Partial<StoredComparisonSelection> | null;
    if (!raw || typeof raw !== 'object') { return; }

    const ids = (value: unknown): number[] => Array.isArray(value)
      ? value.filter((id): id is number => typeof id === 'number' && Number.isFinite(id))
      : [];

    this.comparisonSuiteId = typeof raw.suiteId === 'number' && Number.isFinite(raw.suiteId)
      ? raw.suiteId
      : null;
    this.comparisonPricingBasis = raw.pricingBasis === 'AsRun' ? 'AsRun' : 'Current';
    this.comparisonRunIds = ids(raw.runIds);
    this.comparisonGroupIds = ids(raw.groupIds);
    // A record written before battery results restores none. A record that somehow mixes the two
    // keeps the runs and groups, since the server refuses the mix.
    const batteryRunIds = ids(raw.batteryRunIds);
    this.comparisonBatteryRunIds = this.comparisonRunIds.length + this.comparisonGroupIds.length > 0
      ? []
      : batteryRunIds;
    this.pruneComparisonSelection();
  }

  /** Drops selected ids the loaded lists do not carry. Called as each list arrives. */
  pruneComparisonSelection(): void {
    if (this.workspace.historyRuns.length > 0) {
      const known = new Set(this.comparisonRunOptions.map(run => run.id));
      this.comparisonRunIds = this.comparisonRunIds.filter(id => known.has(id));
    }
    if (this.workspace.runGroups.length > 0) {
      const known = new Set(this.comparisonGroupOptions.map(group => group.id));
      this.comparisonGroupIds = this.comparisonGroupIds.filter(id => known.has(id));
    }
    if (this.workspace.batteryRuns.length > 0) {
      const known = new Set(this.comparisonBatteryRunOptions.map(battery => battery.id));
      this.comparisonBatteryRunIds = this.comparisonBatteryRunIds.filter(id => known.has(id));
    }
  }

  ngOnDestroy(): void {
    this.comparisonSubscription?.unsubscribe();
  }
}
