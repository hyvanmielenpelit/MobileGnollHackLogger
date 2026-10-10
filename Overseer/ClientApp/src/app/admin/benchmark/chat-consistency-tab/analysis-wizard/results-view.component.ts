import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  OnInit,
  Output,
  SimpleChanges,
  ViewChild,
  inject
} from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { SystemService } from '../../../../services/system.service';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import { COPY_STATUS_MS } from '../../benchmark.models';
import { analysisChartPoints, prefersReducedMotion } from '../chat-consistency-charts';
import {
  CcEventDay,
  CcEventGroup,
  CcTaggedAnnotation,
  buildEventDays,
  groupOverseerEvents,
  servedModelChanges
} from '../chat-consistency-events';
import { formatUtcDateTime, gradeText } from '../chat-consistency-format';
import {
  CcNotComputableGroup,
  CcResultKeyFigure,
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
  CcTimelinePoint
} from '../chat-consistency.models';
import { CcEventListComponent } from '../event-list/cc-event-list.component';
import {
  CcAttributionGroupView,
  CcDecisiveChange,
  CcResultsImageContext,
  ccAttributionView,
  ccComputedEndpoints,
  ccRangeBoundsText,
  ccResultsImageItems,
  ccRunMark,
  ccRunSelectionMarks,
  ccShownRunSelection,
  ccUnanalyzedGroups
} from '../results-image/results-image-blocks';
import {
  CcResultsImageDialogComponent,
  CcResultsImageItems,
  CcResultsImageMeasurer
} from '../results-image/results-image-dialog.component';
import {
  CcResultsImageAction,
  CcResultsImageRequest,
  exportResultsImage,
  measureResultsImage
} from '../results-image/results-image-export';
import {
  CC_RESULTS_IMAGE_SECTIONS,
  CcResultsImageSettings,
  ccResultsImageFormatLabel,
  readStoredCcResultsImageSettings,
  writeStoredCcResultsImageSettings
} from '../results-image/results-image-settings';
import { CcEndpointCardComponent } from './endpoint-card/endpoint-card.component';
import { CcNextRunsComponent } from './next-runs/next-runs.component';
import { CcResultPeriodsComponent } from './result-periods/result-periods.component';
import { CcVerdictBannerComponent } from './verdict-banner/verdict-banner.component';

export { CC_ATTRIBUTION_GROUPS, CC_UNANALYZED_REASONS } from '../results-image/results-image-blocks';
export type { CcAttributionGroupView, CcDecisiveChange } from '../results-image/results-image-blocks';

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

/**
 * The Results step of the analysis, in the tabs *Summary* (the verdict banner and the key figures),
 * *Verdicts* (a card per computed endpoint, the not-computable endpoints in one card), *Periods* (the stored
 * periods and their units), *Attribution*, *Next runs* and *Details* (the run selection, the events
 * in the analyzed span, the limitations, the data quality and the analysis's identity, each behind a
 * closed disclosure). Every panel is rendered once and hidden while another tab shows. Beside the tabs,
 * **Copy** and **Download** export the shown section as an image, and **Image settings** opens the
 * dialog that chooses what the images show and the file they are written as.
 */
@Component({
  selector: 'app-cc-results-view',
  standalone: true,
  imports: [
    CcEndpointCardComponent,
    CcEventListComponent,
    CcNextRunsComponent,
    CcResultPeriodsComponent,
    CcResultsImageDialogComponent,
    CcVerdictBannerComponent
  ],
  templateUrl: './results-view.component.html',
  styleUrls: ['./results-view.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcResultsViewComponent implements OnInit, OnChanges, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly systemService = inject(SystemService);

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

  @ViewChild(CcResultsImageDialogComponent) imageDialog?: CcResultsImageDialogComponent;

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
  /** The sides with attributions, in `CC_ATTRIBUTION_GROUPS` order. */
  attributionGroups: CcAttributionGroupView[] = [];
  /** Nothing decisive and nothing attributed beyond *Undetermined*. */
  attributionEmpty = false;
  /** `Nothing is attributed to infrastructure.`; empty when every side has an attribution. */
  unattributedText = '';
  /** The composite events, annotations and served-model changes of the analysis, by day. */
  eventDays: CcEventDay[] = [];
  eventCount = 0;

  /** A copy or download is being composed; both buttons refuse another. */
  exporting = false;
  /** The last export's outcome, cleared after `COPY_STATUS_MS`. */
  exportStatus = '';
  /** `PNG` or `WebP`, the format a download writes; refreshed on every open, change and export. */
  downloadFormat: 'PNG' | 'WebP' = ccResultsImageFormatLabel(readStoredCcResultsImageSettings().format);

  /** The footer's Overseer build, fetched on the first export. */
  private overseerVersion: string | null = null;
  private statusTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

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
    if (changes['result'] && !changes['result'].firstChange) {
      // The dialog's item lists belong to the result it was opened on.
      this.imageDialog?.close();
    }
  }

  ngOnDestroy(): void {
    this.clearStatusTimer();
  }

  private buildVerdicts(): void {
    const endpoints = this.result.endpoints;
    this.computedEndpoints = ccComputedEndpoints(this.result);
    this.notComputableGroups = ccNotComputableGroups(endpoints);
    this.notComputableCount = endpoints.filter(endpoint => !endpoint.computed).length;
  }

  private buildAttribution(): void {
    const view = ccAttributionView(this.result);
    this.decisiveChanges = view.decisiveChanges;
    this.attributionGroups = view.groups;
    this.attributionEmpty = view.empty;
    this.unattributedText = view.unattributedText;
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

  // --- Section images ---

  /** The shown tab's name: `Next runs`. */
  get tabLabel(): string {
    return this.tabs.find(entry => entry.id === this.tab)?.label ?? 'Summary';
  }

  /** `analysis #4`, or `this analysis` before it is saved. */
  get analysisName(): string {
    return this.result.analysisId !== null ? `analysis #${this.result.analysisId}` : 'this analysis';
  }

  /** Measures the next image of a section for the dialog's summary, at the settings it is given. */
  readonly measureImage: CcResultsImageMeasurer = (settings, section) => measureResultsImage(this.imageRequest(section, settings));

  /**
   * Copies or downloads the shown section as an image, with the settings read from storage now, and
   * announces the outcome. Refuses while another export runs.
   */
  async exportSection(action: CcResultsImageAction): Promise<void> {
    if (this.exporting) return;
    this.exporting = true;
    this.cdr.markForCheck();
    try {
      if (this.overseerVersion === null) {
        this.overseerVersion = await firstValueFrom(this.systemService.getVersion()).catch(() => 'unknown');
      }
      const settings = readStoredCcResultsImageSettings();
      this.downloadFormat = ccResultsImageFormatLabel(settings.format);
      this.announce(await exportResultsImage(action, this.imageRequest(this.tab, settings)));
    } finally {
      this.exporting = false;
      this.cdr.markForCheck();
    }
  }

  /** Opens Image settings on the shown section, with every section's items from this result. */
  openImageSettings(opener: HTMLElement): void {
    const context = this.imageContext();
    const items = {} as Record<CcResultsTab, ReturnType<typeof ccResultsImageItems>>;
    for (const section of CC_RESULTS_IMAGE_SECTIONS) {
      items[section] = ccResultsImageItems(section, this.result, context);
    }
    const settings = readStoredCcResultsImageSettings();
    this.downloadFormat = ccResultsImageFormatLabel(settings.format);
    this.imageDialog?.open(this.tab, items as CcResultsImageItems, settings, opener);
    this.cdr.markForCheck();
  }

  onImageSettingsChange(settings: CcResultsImageSettings): void {
    writeStoredCcResultsImageSettings(settings);
    this.downloadFormat = ccResultsImageFormatLabel(settings.format);
    this.cdr.markForCheck();
  }

  private imageContext(): CcResultsImageContext {
    return { rows: this.rows, batteryRows: this.batteryRows, eventDays: this.eventDays };
  }

  private imageRequest(section: CcResultsTab, settings: CcResultsImageSettings): CcResultsImageRequest {
    return { section, result: this.result, context: this.imageContext(), overseerVersion: this.overseerVersion, settings };
  }

  private announce(message: string): void {
    this.clearStatusTimer();
    this.exportStatus = message;
    this.statusTimer = setTimeout(() => {
      this.exportStatus = '';
      this.statusTimer = null;
      this.cdr.markForCheck();
    }, COPY_STATUS_MS);
    this.cdr.markForCheck();
  }

  private clearStatusTimer(): void {
    if (this.statusTimer !== null) {
      clearTimeout(this.statusTimer);
      this.statusTimer = null;
    }
  }

  // --- Run selection ---

  /** The recorded run selection; null for an analysis saved before it was recorded, with nothing to show. */
  get runSelection(): CcRunSelectionView | null {
    return ccShownRunSelection(this.result);
  }

  /** `2026-09-01 00:00 UTC to the last run`, the UTC bounds of the step-1 dates; empty when both are open. */
  rangeBoundsText(selection: CcRunSelectionView): string {
    return ccRangeBoundsText(selection);
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
    return ccRunMark(this.result, runId);
  }

  firstMark(selection: CcRunSelectionView): string {
    return ccRunSelectionMarks(this.result, selection).first;
  }

  lastMark(selection: CcRunSelectionView): string {
    return ccRunSelectionMarks(this.result, selection).last;
  }

  leftOutText(selection: CcRunSelectionView): string {
    return ccRunSelectionMarks(this.result, selection).leftOut;
  }

  /**
   * The unanalyzed runs by reason, in the server's reason order: `#45 (baseline), #51 (comparison)`,
   * with the battery run a reason applies to: `#98 (baseline, battery run #12)`.
   */
  unanalyzedGroups(selection: CcRunSelectionView): { reason: string; label: string; runs: string }[] {
    return ccUnanalyzedGroups(selection);
  }
}
