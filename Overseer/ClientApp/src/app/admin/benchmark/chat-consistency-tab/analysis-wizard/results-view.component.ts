import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject
} from '@angular/core';

import { refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import { analysisChartPoints, prefersReducedMotion } from '../chat-consistency-charts';
import {
  CcEventDay,
  CcEventGroup,
  CcTaggedAnnotation,
  buildEventDays,
  groupOverseerEvents,
  servedModelChanges
} from '../chat-consistency-events';
import { formatUtcDateTime, gradeText, verdictText } from '../chat-consistency-format';
import {
  CcEndpointStatus,
  CcNotComputableGroup,
  CcResultKeyFigure,
  ccEndpointStatus,
  ccNextRunGroups,
  ccNotComputableGroups,
  ccResultKeyFigures
} from '../chat-consistency-results';
import {
  CcAnalysisResult,
  CcAttributionResult,
  CcBatteryRunRow,
  CcBatteryTimelinePoint,
  CcEndpointResult,
  CcRunRow,
  CcRunSelectionView,
  CcTimelinePoint,
  CcUnanalyzedReason,
  CcVerdict
} from '../chat-consistency.models';
import { CcEventListComponent } from '../event-list/cc-event-list.component';
import { CcEndpointCardComponent } from './endpoint-card/endpoint-card.component';
import { CcNextRunsComponent } from './next-runs/next-runs.component';
import { CcResultPeriodsComponent } from './result-periods/result-periods.component';
import { CcVerdictBannerComponent } from './verdict-banner/verdict-banner.component';

/** The attribution groups, in the order the results show them. */
export const CC_ATTRIBUTION_GROUPS: readonly { readonly side: string; readonly title: string }[] = [
  { side: 'ours', title: 'Our changes' },
  { side: 'provider', title: 'Provider' },
  { side: 'infrastructure', title: 'Infrastructure' },
  { side: 'undetermined', title: 'Undetermined' }
];

/** Why a usable run in the periods was not analyzed, in the order the server classifies it. */
export const CC_UNANALYZED_REASONS: readonly { readonly reason: CcUnanalyzedReason; readonly label: string }[] = [
  { reason: 'leftOut', label: 'Left out in step 1' },
  { reason: 'outsideDateRange', label: 'Outside the step-1 dates' },
  { reason: 'beforeFirstRun', label: 'Before the first run' },
  { reason: 'afterLastRun', label: 'After the last run' },
  { reason: 'notSelected', label: 'Not assigned to a period' },
  { reason: 'outsideComparisonSet', label: 'Outside the compared set' }
];

export type CcResultsTab = 'summary' | 'verdicts' | 'periods' | 'attribution' | 'nextRuns' | 'details';

/** The Results step's tabs, in order. */
export const CC_RESULTS_TABS: readonly { readonly id: CcResultsTab; readonly label: string }[] = [
  { id: 'summary', label: 'Summary' },
  { id: 'verdicts', label: 'Verdicts' },
  { id: 'periods', label: 'Periods' },
  { id: 'attribution', label: 'Attribution' },
  { id: 'nextRuns', label: 'Next runs' },
  { id: 'details', label: 'Details' }
];

/** The Results step's selected tab, per browser. Read and written in `try/catch`. */
export const CC_RESULTS_STORAGE_KEY = 'overseer.benchmark.chatConsistency.results';

/** The version of the stored record; a record of any other version is ignored. */
const CC_RESULTS_STORAGE_VERSION = 2;

/** The stored tab; a missing, unknown, damaged or version-1 value is *Summary*. */
export function readStoredResultsTab(): CcResultsTab {
  try {
    const raw = localStorage.getItem(CC_RESULTS_STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      const record = parsed as Record<string, unknown>;
      const known = CC_RESULTS_TABS.find(entry => entry.id === record['tab']);
      if (record['version'] === CC_RESULTS_STORAGE_VERSION && known) return known.id;
    }
  } catch {
    // Private mode, blocked storage or a damaged value: the default.
  }
  return 'summary';
}

function writeStoredResultsTab(tab: CcResultsTab): void {
  try {
    localStorage.setItem(CC_RESULTS_STORAGE_KEY, JSON.stringify({ version: CC_RESULTS_STORAGE_VERSION, tab }));
  } catch {
    // Private mode or blocked storage: the tab still applies for this session.
  }
}

/** The order of the computed endpoint cards: changes first, inconclusive last. */
const STATUS_ORDER: Readonly<Record<CcEndpointStatus, number>> = {
  changed: 0,
  improved: 1,
  within: 2,
  inconclusive: 3,
  notComputable: 4
};

const DECISIVE_VERDICTS: readonly CcVerdict[] = ['changedDegraded', 'changedImproved'];

/** The sides an attribution names, as the line about the sides without one reads them. */
const ATTRIBUTED_SIDE_NAMES: readonly { readonly side: string; readonly name: string }[] = [
  { side: 'ours', name: 'our changes' },
  { side: 'provider', name: 'the provider' },
  { side: 'infrastructure', name: 'infrastructure' }
];

/** A decisive change the attribution explains. */
export interface CcDecisiveChange {
  id: string;
  name: string;
  verdict: CcVerdict;
  verdictText: string;
}

/** The attributions of one side. */
export interface CcAttributionGroupView {
  side: string;
  title: string;
  attributions: CcAttributionResult[];
}

/** `a`, `a or b`, `a, b or c`. */
function orList(items: readonly string[]): string {
  if (items.length <= 1) return items.join('');
  return `${items.slice(0, -1).join(', ')} or ${items[items.length - 1]}`;
}

/**
 * The Results step of the analysis, in the tabs *Summary* (the verdict banner and the key figures),
 * *Verdicts* (a card per computed endpoint, the not-computable endpoints in one card), *Periods* (the stored
 * periods and their units), *Attribution*, *Next runs* and *Details* (the run selection, the events
 * in the analyzed span, the limitations, the data quality and the analysis's identity, each behind a
 * closed disclosure). Every panel is rendered once and hidden while another tab shows.
 */
@Component({
  selector: 'app-cc-results-view',
  standalone: true,
  imports: [
    CcEndpointCardComponent,
    CcEventListComponent,
    CcNextRunsComponent,
    CcResultPeriodsComponent,
    CcVerdictBannerComponent
  ],
  templateUrl: './results-view.component.html',
  styleUrls: ['./results-view.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcResultsViewComponent implements OnChanges {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  @Input({ required: true }) result!: CcAnalysisResult;
  /** The subject's timeline points; the event list keeps the analysis's runs. */
  @Input() points: readonly CcTimelinePoint[] = [];
  /** The timeline's battery points; a battery analysis's event list keeps its battery runs. */
  @Input() batteryPoints: readonly CcBatteryTimelinePoint[] = [];
  /** The timeline's composite events, whose E numbers the results reuse. */
  @Input() eventNumbering: readonly CcEventGroup[] = [];
  /** The step-1 runs, for the period cards and the next runs' suite names. */
  @Input() rows: readonly CcRunRow[] = [];
  /** The step-1 battery runs, for a battery analysis's period cards. */
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  /** The timeline's annotations, tagged `A1`…, for the period cards' markers. */
  @Input() annotations: readonly CcTaggedAnnotation[] = [];

  @Output() readonly repeatSetup = new EventEmitter<number>();
  @Output() readonly openRunReport = new EventEmitter<number>();
  @Output() readonly openBatteryRunReport = new EventEmitter<number>();

  readonly tabs = CC_RESULTS_TABS;
  tab: CcResultsTab = readStoredResultsTab();

  keyFigures: CcResultKeyFigure[] = [];
  /** The computed endpoints: changed, improved, within margin, inconclusive, then by id. */
  computedEndpoints: CcEndpointResult[] = [];
  notComputableGroups: CcNotComputableGroup[] = [];
  notComputableCount = 0;
  /** The next-run cards the *Next runs* tab shows. */
  nextRunCardCount = 0;
  decisiveChanges: CcDecisiveChange[] = [];
  /** The sides with attributions, in {@link CC_ATTRIBUTION_GROUPS} order. */
  attributionGroups: CcAttributionGroupView[] = [];
  /** Nothing decisive and nothing attributed beyond *Undetermined*. */
  attributionEmpty = false;
  /** `Nothing is attributed to infrastructure.`; empty when every side has an attribution. */
  unattributedText = '';
  /** The composite events, annotations and served-model changes of the analysis, by day. */
  eventDays: CcEventDay[] = [];
  eventCount = 0;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['result'] || changes['rows']) {
      this.buildVerdicts();
      this.buildAttribution();
      this.keyFigures = ccResultKeyFigures(this.result);
      this.nextRunCardCount = ccNextRunGroups(this.result, this.rows).length;
    }
    if (changes['result'] || changes['points'] || changes['batteryPoints'] || changes['eventNumbering']) {
      // The served-model changes of the analyzed units; every timeline point serves the events' harness lookup.
      const { points } = analysisChartPoints(this.result, this.points, this.batteryPoints);
      this.eventDays = buildEventDays(
        groupOverseerEvents(this.result.events, this.points, this.eventNumbering),
        this.result.annotations, servedModelChanges(points));
      this.eventCount = this.eventDays.reduce((sum, day) => sum + day.items.length, 0);
    }
  }

  private buildVerdicts(): void {
    const endpoints = this.result.endpoints;
    this.computedEndpoints = endpoints
      .filter(endpoint => endpoint.computed)
      .map(endpoint => ({ endpoint, rank: STATUS_ORDER[ccEndpointStatus(endpoint)] }))
      .sort((a, b) => a.rank - b.rank || a.endpoint.id.localeCompare(b.endpoint.id, undefined, { numeric: true }))
      .map(entry => entry.endpoint);
    this.notComputableGroups = ccNotComputableGroups(endpoints);
    this.notComputableCount = endpoints.filter(endpoint => !endpoint.computed).length;
  }

  private buildAttribution(): void {
    const byId = new Map(this.result.endpoints.map(endpoint => [endpoint.id, endpoint] as const));
    const decisive: CcDecisiveChange[] = [];
    for (const change of this.result.attribution.totalChanges) {
      const endpoint = byId.get(change.endpointId);
      if (!endpoint?.verdict || !DECISIVE_VERDICTS.includes(endpoint.verdict)) continue;
      if (decisive.some(entry => entry.id === endpoint.id)) continue;
      decisive.push({
        id: endpoint.id,
        name: change.name || endpoint.name,
        verdict: endpoint.verdict,
        verdictText: verdictText(endpoint.verdict, endpoint.verdictLabel)
      });
    }
    this.decisiveChanges = decisive;
    this.attributionGroups = CC_ATTRIBUTION_GROUPS
      .map(group => ({ side: group.side, title: group.title, attributions: this.attributionsOf(group.side) }))
      .filter(group => group.attributions.length > 0);
    const attributed = this.result.attribution.attributions.some(attribution => attribution.side !== 'undetermined');
    this.attributionEmpty = decisive.length === 0 && !attributed;
    const missing = ATTRIBUTED_SIDE_NAMES
      .filter(entry => !this.attributionGroups.some(group => group.side === entry.side))
      .map(entry => entry.name);
    this.unattributedText = missing.length > 0 ? `Nothing is attributed to ${orList(missing)}.` : '';
  }

  attributionsOf(side: string): CcAttributionResult[] {
    return this.result.attribution.attributions.filter(attribution => attribution.side === side);
  }

  /** The undetermined attributions, whose evidence the empty state shows. */
  get undeterminedAttributions(): CcAttributionResult[] {
    return this.attributionsOf('undetermined');
  }

  /** `P2 Time to first answer text · P3 Answer streaming rate`. */
  groupNames(group: CcNotComputableGroup): string {
    return group.endpoints.map(endpoint => `${endpoint.id} ${endpoint.name}`).join(' · ');
  }

  grade(value: string): string {
    return gradeText(value);
  }

  dateTime(value: string | null): string {
    return formatUtcDateTime(value);
  }

  // --- Tabs ---

  selectTab(tab: CcResultsTab): void {
    if (tab === this.tab) return;
    this.tab = tab;
    writeStoredResultsTab(tab);
    this.cdr.detectChanges();
    refreshAnchorPositioning();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const next = this.rovingTabIndex(event, index, this.tabs.length);
    if (next === null) return;
    const tab = this.tabs[next].id;
    this.selectTab(tab);
    document.getElementById(`cc-res-tab-${tab}`)?.focus();
  }

  /** *See the next runs*: the Next runs tab, focus on its panel. */
  openNextRuns(): void {
    this.selectTab('nextRuns');
    document.getElementById('cc-res-panel-nextRuns')?.focus({ preventScroll: true });
  }

  /**
   * An endpoint chosen on the banner: the Verdicts tab, its card (or the not-computable card) scrolled
   * into view (block 'nearest'; smooth only without prefers-reduced-motion) and focused.
   */
  onEndpointSelected(id: string): void {
    this.selectTab('verdicts');
    const hasCard = this.computedEndpoints.some(endpoint => endpoint.id === id);
    const target = this.host.nativeElement.querySelector<HTMLElement>(
      hasCard ? `[id="cc-ep-${id}"]` : '#cc-ep-uncomputed');
    if (!target) return;
    target.scrollIntoView({ block: 'nearest', behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
    target.focus({ preventScroll: true });
  }

  /** The §5 tab keyboard model's target index, or null for a key it does not handle. */
  private rovingTabIndex(event: KeyboardEvent, index: number, count: number): number | null {
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: count - 1
    };
    const requested = targets[event.key];
    if (requested === undefined) return null;
    event.preventDefault();
    return (requested + count) % count;
  }

  // --- Run selection ---

  /** The recorded run selection; null for an analysis saved before it was recorded, with nothing to show. */
  get runSelection(): CcRunSelectionView | null {
    const selection = this.result.runSelection;
    return selection && (selection.recorded || selection.unanalyzedRuns.length > 0) ? selection : null;
  }

  /** `2026-09-01 00:00 UTC to the last run`, the UTC bounds of the step-1 dates; empty when both are open. */
  rangeBoundsText(selection: CcRunSelectionView): string {
    if (!selection.rangeFromUtc && !selection.rangeToUtc) return '';
    const from = selection.rangeFromUtc ? formatUtcDateTime(selection.rangeFromUtc) : 'the first run';
    const to = selection.rangeToUtc ? formatUtcDateTime(selection.rangeToUtc) : 'the last run';
    return `${from} to ${to}`;
  }

  /** The analysis counted battery runs: its marks and left-out ids are battery runs'. */
  get batteryAnalysis(): boolean {
    return this.result.unitKind === 'batteryRun';
  }

  /** The units of a period: `2 battery runs` in a battery analysis, empty otherwise. */
  periodUnitsText(period: string): string {
    if (!this.batteryAnalysis) return '';
    const count = (this.result.units ?? []).filter(unit => unit.period === period).length;
    return `${count} ${count === 1 ? 'battery run' : 'battery runs'}`;
  }

  /** `#102`, or `battery run #12` in a battery analysis; `none` without a mark. */
  runMark(runId: number | null | undefined): string {
    if (runId === null || runId === undefined) return 'none';
    return this.batteryAnalysis ? `battery run #${runId}` : `#${runId}`;
  }

  firstMark(selection: CcRunSelectionView): string {
    return this.runMark(this.batteryAnalysis ? selection.firstBatteryRunId : selection.firstRunId);
  }

  lastMark(selection: CcRunSelectionView): string {
    return this.runMark(this.batteryAnalysis ? selection.lastBatteryRunId : selection.lastRunId);
  }

  leftOutText(selection: CcRunSelectionView): string {
    const ids = this.batteryAnalysis ? selection.leftOutBatteryRunIds ?? [] : selection.leftOutRunIds;
    return ids.length > 0 ? ids.map(id => this.runMark(id)).join(', ') : 'none';
  }

  /**
   * The unanalyzed runs by reason, in the server's reason order: `#45 (baseline), #51 (comparison)`,
   * with the battery run a reason applies to: `#98 (baseline, battery run #12)`.
   */
  unanalyzedGroups(selection: CcRunSelectionView): { reason: string; label: string; runs: string }[] {
    return CC_UNANALYZED_REASONS
      .map(({ reason, label }) => ({
        reason,
        label,
        runs: selection.unanalyzedRuns
          .filter(run => run.reason === reason)
          .map(run => run.batteryRunId !== null && run.batteryRunId !== undefined
            ? `#${run.runId} (${run.period}, battery run #${run.batteryRunId})`
            : `#${run.runId} (${run.period})`)
          .join(', ')
      }))
      .filter(group => group.runs !== '');
  }
}
