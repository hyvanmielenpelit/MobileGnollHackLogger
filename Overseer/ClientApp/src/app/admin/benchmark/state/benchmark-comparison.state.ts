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
   * Replaces the selection with a preset's battery results, so the wizard opens on step 1 with
   * them selected: runs and groups are cleared (the server refuses the mix), and the comparison on
   * hand is dropped, which is what returns the wizard to step 1. Ids are de-duplicated in order.
   * The host calls this before opening the wizard.
   */
  applyComparisonPreset(preset: ComparisonWizardPreset): void {
    const batteryRunIds = [...new Set(preset.batteryRunIds)];
    this.onComparisonSelectionChange({ runIds: [], groupIds: [], batteryRunIds });
    // The picker's rows are the battery runs Run History loads, which a shortcut from another tab
    // may not have loaded yet.
    const loaded = new Set(this.workspace.batteryRuns.map(battery => battery.id));
    if (batteryRunIds.some(id => !loaded.has(id))) {
      this.workspace.loadHistory();
    }
    this.viewSync.notify();
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
