import { Component, ChangeDetectorRef, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AdminBenchmarkService,
  BenchmarkBatteryRunDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';
import { CardListChip, CardListFacet } from '../../../shared/data-table/card-list-state';
import { FilterFacetComponent } from '../../../shared/data-table/filter-facet.component';
import { ProviderBadgeComponent } from '../../../shared/provider-badge/provider-badge.component';
import { InfoTipComponent } from '../../../shared/info-tip/info-tip.component';
import {
  RunFactBadge,
  runFactBadges
} from '../run-report-frame/run-facts';
import {
  RUN_HISTORY_SORTS,
  RUN_HISTORY_LIMIT,
  BATTERY_RUN_HISTORY_LIMIT,
  FINGERPRINT_LONG_NAMES,
  BenchmarkFingerprintEntry,
  HistoryItem
} from '../benchmark.models';
import {
  formatCostAmount,
  formatRunEstimatedCost,
  formatStatusLabel,
  statusBadgeClass,
  batteryStatusBadgeClass,
  getScoreBadgeClass,
  isAbortedRun,
  runDurationMs,
  answerShortfallOf,
  formatDuration
} from '../benchmark-run-format';
import {
  batteryModelBadges,
  batteryModelName,
  batteryRunStatusLabel,
  formatIndexWithHalfWidth,
  formatNumber,
  httpErrorText,
  isLiveBatteryRunStatus
} from '../batteries/battery.models';
import { BenchmarkWorkspaceStore, batteryRunDurationMs } from '../state/benchmark-workspace.store';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/** One hash of a battery run's instrument strip. */
export interface BatteryFingerprintEntry {
  label: 'DEF' | 'CLASS';
  cssClass: 'fp-def' | 'fp-class';
  /** Read after the short label by assistive technology. */
  longName: string;
  short: string;
  /** The info tip's term and value. */
  term: string;
  value: string;
}

/** The Run History sub-tab: the filterable list of run and battery run cards. */
@Component({
  selector: 'app-benchmark-history-tab',
  standalone: true,
  imports: [
    CommonModule, FilterFacetComponent, ProviderBadgeComponent, InfoTipComponent
  ],
  templateUrl: './benchmark-history-tab.component.html',
  styleUrls: ['./benchmark-history-tab.component.scss']
})
export class BenchmarkHistoryTabComponent implements OnInit {
  private readonly viewSync = inject(BenchmarkViewSync);
  readonly bridge = inject(BenchmarkShellBridge);
  readonly workspace = inject(BenchmarkWorkspaceStore);
  private readonly monitor = inject(BenchmarkActiveRunMonitor);
  private cdr = inject(ChangeDetectorRef);
  private benchmarkService = inject(AdminBenchmarkService);
  readonly formatStatusLabel = formatStatusLabel;
  readonly formatDuration = formatDuration;
  readonly runDurationMs = runDurationMs;
  readonly formatCostAmount = formatCostAmount;
  readonly formatRunEstimatedCost = formatRunEstimatedCost;
  readonly statusBadgeClass = statusBadgeClass;
  readonly getScoreBadgeClass = getScoreBadgeClass;
  readonly isAbortedRun = isAbortedRun;
  readonly answerShortfallOf = answerShortfallOf;
  readonly batteryStatusBadgeClass = batteryStatusBadgeClass;
  readonly batteryRunStatusLabel = batteryRunStatusLabel;
  readonly batteryRunDurationMs = batteryRunDurationMs;
  readonly formatIndexWithHalfWidth = formatIndexWithHalfWidth;
  readonly formatNumber = formatNumber;

  @ViewChild('deleteBatteryDialog') deleteBatteryDialog?: ElementRef<HTMLDialogElement>;

  /** The battery run the delete confirmation asks about; null while it is closed. */
  pendingBatteryDelete: BenchmarkBatteryRunDto | null = null;

  /** The confirmation's *Also delete its member runs*; unchecked whenever it opens. */
  deleteBatteryMembers = false;

  deletingBattery = false;

  deleteBatteryError: string | null = null;

  constructor() {
    // Service state changes outside this component's own events; OnPush needs telling.
    this.viewSync.changed$.pipe(takeUntilDestroyed()).subscribe(() => {
      this.cdr.markForCheck();
      this.cdr.detectChanges();
    });
  }

  ngOnInit(): void {
    this.workspace.loadHistory();
    // The run groups load with it, so a remembered Model Comparison selection is checked against both lists.
    this.workspace.loadRunGroups();
  }

  /** The orders Sort by offers. */
  readonly historySorts = RUN_HISTORY_SORTS;

  /** Nothing is recorded: no run and no battery run came back. */
  get historyEmpty(): boolean {
    return this.workspace.historyRuns.length === 0 && this.workspace.batteryRuns.length === 0;
  }

  /** The cards on screen: the loaded runs and battery runs filtered, sorted and cut to the batch. */
  get historyView(): HistoryItem[] {
    return this.workspace.historyList.view(this.workspace.historyItems);
  }

  /** The listed facets of the filter bar, memoized on `historyItems` and the list's revision. */
  get historyFacets(): CardListFacet[] {
    return this.workspace.historyList.facets(this.workspace.historyItems);
  }

  /** The removable chips of the active filters. */
  get historyChips(): CardListChip[] {
    return this.workspace.historyList.chips(this.workspace.historyItems);
  }

  /**
   * The list's status line, in which a battery run counts as one run, with a note for each endpoint
   * whose limit was reached and one when the battery runs did not load.
   */
  get historyListStatus(): string {
    const text = this.workspace.historyList.statusText(this.workspace.historyItems, { one: 'run', many: 'runs' });
    if (!text) {
      return text;
    }
    let status = text;
    if (this.workspace.historyRuns.length >= RUN_HISTORY_LIMIT) {
      status += ` · Only the newest ${RUN_HISTORY_LIMIT} runs are loaded`;
    }
    if (this.workspace.batteryRuns.length >= BATTERY_RUN_HISTORY_LIMIT) {
      status += ` · newest ${BATTERY_RUN_HISTORY_LIMIT} battery runs`;
    }
    if (this.workspace.batteryRunsFailed) {
      status += ' · Battery runs could not be loaded';
    }
    return status;
  }

  /** The statuses actually present in the loaded history, so a retired status drops out on its own. */
  get historyStatusOptions(): string[] {
    const seen = new Set<string>();
    for (const run of this.workspace.historyRuns) {
      seen.add(formatStatusLabel(run.status));
    }
    return Array.from(seen).sort();
  }

  /** The id of a card's title, which Load more and a delete focus. */
  historyTitleId(item: HistoryItem): string {
    return item.kind === 'run' ? `rh-run-${item.run.id}-title` : `rh-battery-${item.battery.id}-title`;
  }

  /** The tested model's badges per run, built once per run object. */
  private readonly historyBadgeCache = new WeakMap<BenchmarkRunSummaryDto, RunFactBadge[]>();

  /** The tested model's badges on a Run History card: the run report's rules, without a service tier. */
  historyModelBadges(run: BenchmarkRunSummaryDto): RunFactBadge[] {
    let badges = this.historyBadgeCache.get(run);
    if (!badges) {
      badges = runFactBadges({
        name: run.testedModelDisplayNameUsed || run.testedModelIdUsed || 'not recorded',
        provider: run.testedModelProviderUsed || null,
        thinkingLevel: run.testedModelThinkingLevelUsed ?? null,
        reasoningMode: run.testedModelReasoningModeUsed ?? null,
        serviceTier: null,
        customEndpoint: false
      });
      this.historyBadgeCache.set(run, badges);
    }
    return badges;
  }

  /** A battery card's headline: the tested model's label, else its id. */
  readonly batteryModelName = batteryModelName;

  /** The tested model's badges on a battery card, by the same rules as a run card's. */
  readonly batteryModelBadges = batteryModelBadges;

  /** The orchestrator may still move the battery run, so it can be neither deleted nor resumed here. */
  isBatteryLive(battery: BenchmarkBatteryRunDto): boolean {
    return isLiveBatteryRunStatus(battery.status) || battery.isDriving;
  }

  /** Show progress is offered while the battery run is live or can be resumed. */
  canShowBatteryProgress(battery: BenchmarkBatteryRunDto): boolean {
    return this.isBatteryLive(battery) || battery.resumable;
  }

  /** The Intelligence metric's second line when there is no Overall Index; null when it needs none. */
  batteryIncompleteNote(battery: BenchmarkBatteryRunDto): string | null {
    return battery.completedSuiteCount < battery.suiteCount || battery.latestAnalysisComplete === false
      ? `Incomplete (${battery.completedSuiteCount} of ${battery.suiteCount} suites)`
      : null;
  }

  /** The distinct runs a battery run's member rows point at, those already deleted left out. */
  batteryMemberRunCount(battery: BenchmarkBatteryRunDto): number {
    return new Set(battery.members.filter(member => member.runStatus !== 'Deleted').map(member => member.runId)).size;
  }

  private readonly batteryFingerprintCache = new WeakMap<BenchmarkBatteryRunDto, BatteryFingerprintEntry[]>();

  /** A battery card's two hashes: the definition and the latest analysis's comparability class. */
  batteryFingerprints(battery: BenchmarkBatteryRunDto): BatteryFingerprintEntry[] {
    let entries = this.batteryFingerprintCache.get(battery);
    if (!entries) {
      const classSha = battery.comparabilityClassSha256 ?? null;
      entries = [
        {
          label: 'DEF',
          cssClass: 'fp-def',
          longName: 'battery definition',
          short: this.shortFingerprint(battery.definitionSha256),
          term: 'Battery definition SHA-256',
          value: battery.definitionSha256 || 'not recorded'
        },
        {
          label: 'CLASS',
          cssClass: 'fp-class',
          longName: 'comparability class',
          short: this.shortFingerprint(classSha),
          term: 'Comparability class SHA-256',
          value: classSha || 'not recorded'
        }
      ];
      this.batteryFingerprintCache.set(battery, entries);
    }
    return entries;
  }

  /** An instrument label's long name, read after the short label by assistive technology. */
  fingerprintLongName(entry: BenchmarkFingerprintEntry): string {
    return FINGERPRINT_LONG_NAMES[entry.label];
  }

  /** A fingerprint's full description split into its name and its value, for the instrument info tip. */
  fingerprintParts(entry: BenchmarkFingerprintEntry): { term: string; value: string } {
    const at = entry.title.indexOf(': ');
    return at < 0
      ? { term: entry.title, value: '' }
      : { term: entry.title.slice(0, at), value: entry.title.slice(at + 2) };
  }

  onHistorySearchInput(event: Event): void {
    this.workspace.historyList.setSearchInput((event.target as HTMLInputElement).value);
  }

  /** Escape with text clears the search at once; in an empty field it passes through. */
  onHistorySearchKeydown(event: KeyboardEvent): void {
    if (this.workspace.historyList.clearSearchOnEscape(event)) {
      this.cdr.detectChanges();
    }
  }

  onHistorySortChange(event: Event): void {
    if (this.workspace.historyList.setSort((event.target as HTMLSelectElement).value)) {
      this.cdr.detectChanges();
    }
  }

  onHistoryFacetChange(column: string, values: string[]): void {
    this.workspace.historyList.setFacet(column, values);
    this.cdr.detectChanges();
  }

  /** Shows or hides the runs that are members of a battery run; not a filter, so Clear all keeps it. */
  toggleBatteryMembers(): void {
    this.workspace.setShowBatteryMembers(!this.workspace.showBatteryMembers);
    this.cdr.detectChanges();
  }

  /** Removes a chip's filter, then focuses the chip now in its place, else the previous one, else the search. */
  removeHistoryChip(chip: CardListChip): void {
    const index = this.workspace.historyList.removeChip(chip, this.workspace.historyItems);
    this.cdr.detectChanges();
    const chips = Array.from(document.querySelectorAll<HTMLButtonElement>('#bm-panel-history .rh-filter-chips .gh-filter-chip'));
    const target = index >= 0 ? chips[index] ?? chips[index - 1] : undefined;
    (target ?? document.getElementById('rh-search'))?.focus();
  }

  /** Clears the search and every filter, then focuses the search. */
  clearHistoryFilters(): void {
    this.workspace.historyList.clearFilters();
    this.cdr.detectChanges();
    document.getElementById('rh-search')?.focus();
  }

  showMoreHistory(): void {
    this.focusHistoryCard(this.workspace.historyList.showMore(this.workspace.historyItems));
  }

  showAllHistory(): void {
    this.focusHistoryCard(this.workspace.historyList.showAll(this.workspace.historyItems));
  }

  /** Renders, then focuses the title of the card at `index` in the view, if there is one. */
  private focusHistoryCard(index: number): void {
    this.cdr.detectChanges();
    const item = this.historyView[index];
    if (item) {
      document.getElementById(this.historyTitleId(item))?.focus();
    }
  }

  /** After a delete: the card now at the deleted one's index, else the previous one, else the list's heading. */
  private focusAfterHistoryDelete(index: number): void {
    const view = this.historyView;
    const item = view[index] ?? view[index - 1];
    const target = item ? document.getElementById(this.historyTitleId(item)) : null;
    (target ?? document.getElementById('rh-list-title'))?.focus();
  }

  /**
   * H2. The first eight hex characters of a run's candidate system-prompt hash — enough to tell two
   * instruments apart at a glance, and short enough to sit in a table cell. The full hash is on the title.
   */
  shortFingerprint(sha: string | null | undefined): string {
    return sha ? sha.substring(0, 8) : '-';
  }

  /**
   * H2. The five fingerprints of a run's instrument, in the fixed order the run list stacks them.
   *
   * The shape is always five rows: a hash that was never recorded shows as `-` under its own label
   * rather than dropping out, so two runs' stacks line up row for row.
   */
  fingerprintEntries(run: BenchmarkRunSummaryDto): BenchmarkFingerprintEntry[] {
    return [
      {
        label: 'PROMPT',
        cssClass: 'fp-prompt',
        short: this.shortFingerprint(run.candidateSystemPromptSha256),
        title: run.candidateSystemPromptSha256
          ? 'Candidate system prompt SHA-256: ' + run.candidateSystemPromptSha256
          : 'Candidate system prompt SHA-256: not recorded'
      },
      {
        label: 'GUIDES',
        cssClass: 'fp-guides',
        short: this.shortFingerprint(run.toolGuidesSha256),
        title: run.toolGuidesSha256
          ? 'Tool guides SHA-256: ' + run.toolGuidesSha256
          : 'Tool guides SHA-256: not recorded'
      },
      {
        label: 'KB',
        cssClass: 'fp-kb',
        short: this.shortFingerprint(run.knowledgeBaseHeadSha),
        title: run.knowledgeBaseHeadSha
          ? 'Knowledge base Git HEAD SHA: ' + run.knowledgeBaseHeadSha
          : 'Knowledge base Git HEAD SHA: not recorded'
      },
      {
        label: 'WIKI',
        cssClass: 'fp-wiki',
        short: this.shortFingerprint(run.wikiHeadSha),
        title: run.wikiHeadSha
          ? 'GnollHack wiki Git HEAD SHA: ' + run.wikiHeadSha
          : 'GnollHack wiki Git HEAD SHA: not recorded'
      },
      {
        label: 'SRC',
        cssClass: 'fp-source',
        short: this.shortFingerprint(run.sourceCodeHeadSha),
        title: run.sourceCodeHeadSha
          ? 'GnollHack source Git HEAD SHA: ' + run.sourceCodeHeadSha
          : 'GnollHack source Git HEAD SHA: not recorded'
      }
    ];
  }

  /** The Run History kicker's *Battery #id · suite s/K*, or null for a run outside any battery run. */
  batteryBadgeLabelOf(run: BenchmarkRunSummaryDto): string | null {
    if (run.batteryRunId == null) return null;
    const position = run.batterySuitePosition != null && run.batterySuiteCount != null
      ? ` · suite ${run.batterySuitePosition}/${run.batterySuiteCount}`
      : '';
    return `Battery #${run.batteryRunId}${position}`;
  }

  downloadReport(runId: number) {
    window.open(this.benchmarkService.getRunReportUrl(runId), '_blank');
  }

  downloadToolCallLog(runId: number) {
    window.open(this.benchmarkService.getToolCallLogUrl(runId), '_blank');
  }

  /** The battery run's deterministic Markdown analysis report; inert until it has an analysis. */
  downloadBatteryReport(battery: BenchmarkBatteryRunDto): void {
    if (battery.latestAnalysisId == null) {
      return;
    }
    window.open(this.benchmarkService.getBatteryReportUrl(battery.id), '_blank');
  }

  /** Opens the battery progress dialog on this battery run. */
  showBatteryProgress(battery: BenchmarkBatteryRunDto): void {
    this.monitor.openBatteryDialog(battery.id);
  }

  deleteRun(runId: number) {
    this.bridge.openConfirmDialog({
      title: 'Delete Benchmark Run',
      message: `Are you sure you want to delete benchmark run #${runId}?`,
      dangerNotice: 'This action is permanent and cannot be undone.',
      buttonText: 'Delete Run',
      buttonClass: 'btn-gh btn-gh-delete',
      action: () => {
        // Where the run's card was, so focus lands on the card that takes its place.
        const index = this.historyView.findIndex(item => item.kind === 'run' && item.run.id === runId);
        this.benchmarkService.deleteRun(runId).subscribe({
          next: () => {
            this.bridge.runDeleted(runId);
            this.workspace.loadHistory(index >= 0 ? () => this.focusAfterHistoryDelete(index) : undefined);
            this.workspace.loadAllFootprints();
          },
          error: (err) => console.error('Failed to delete run', err)
        });
      }
    });
  }

  /** Asks to delete a battery run; inert while it is live. */
  openDeleteBatteryDialog(battery: BenchmarkBatteryRunDto): void {
    if (this.isBatteryLive(battery)) {
      return;
    }
    this.pendingBatteryDelete = battery;
    this.deleteBatteryMembers = false;
    this.deleteBatteryError = null;
    this.cdr.detectChanges();
    const dialog = this.deleteBatteryDialog?.nativeElement;
    if (dialog && !dialog.open) {
      dialog.showModal();
    }
  }

  closeDeleteBatteryDialog(): void {
    const dialog = this.deleteBatteryDialog?.nativeElement;
    if (dialog?.open) {
      dialog.close();
    }
    this.pendingBatteryDelete = null;
    this.deleteBatteryMembers = false;
    this.deleteBatteryError = null;
    this.cdr.detectChanges();
  }

  /** Escape closes the confirmation, except while the delete is under way. */
  onDeleteBatteryCancel(event: Event): void {
    event.preventDefault();
    if (!this.deletingBattery) {
      this.closeDeleteBatteryDialog();
    }
  }

  onDeleteBatteryMembersChange(event: Event): void {
    this.deleteBatteryMembers = (event.target as HTMLInputElement).checked;
  }

  confirmDeleteBattery(): void {
    const battery = this.pendingBatteryDelete;
    if (!battery || this.deletingBattery) {
      return;
    }
    const deleteMembers = this.deleteBatteryMembers;
    const memberRunIds = deleteMembers
      ? Array.from(new Set(battery.members.filter(member => member.runStatus !== 'Deleted').map(member => member.runId)))
      : [];
    // Where the battery run's card was, so focus lands on the card that takes its place.
    const key = `battery:${battery.id}`;
    const index = this.historyView.findIndex(item => item.key === key);
    this.deletingBattery = true;
    this.deleteBatteryError = null;
    this.cdr.detectChanges();
    this.benchmarkService.deleteBatteryRun(battery.id, deleteMembers).subscribe({
      next: () => {
        this.deletingBattery = false;
        this.closeDeleteBatteryDialog();
        for (const runId of memberRunIds) {
          this.bridge.runDeleted(runId);
        }
        this.workspace.loadHistory(index >= 0 ? () => this.focusAfterHistoryDelete(index) : undefined);
        if (deleteMembers) {
          this.workspace.loadAllFootprints();
        }
      },
      error: (err) => {
        this.deletingBattery = false;
        this.deleteBatteryError = httpErrorText(err, `Could not delete battery run #${battery.id}.`);
        this.cdr.detectChanges();
      }
    });
  }
}
