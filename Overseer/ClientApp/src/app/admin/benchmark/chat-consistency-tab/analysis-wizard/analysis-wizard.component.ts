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
import { Subscription } from 'rxjs';

import { SystemAiConfigDto } from '../../../../services/admin.service';
import { AdminChatConsistencyService, ccErrorText } from '../../../../services/admin-chat-consistency.service';
import { InfoTipComponent } from '../../../../shared/info-tip/info-tip.component';
import { ModelPickerOption } from '../../../../shared/model-picker/model-picker.component';
import { PaneResizerComponent } from '../../../../shared/pane-resizer/pane-resizer.component';
import { ensureOverlayPolyfills, refreshAnchorPositioning } from '../../../../utils/polyfills.util';
import {
  DownloadCenterChatConsistencyContext,
  DownloadCenterPanelComponent
} from '../../download-center/download-center-panel.component';
import {
  formatUtcDate,
  formatUtcDateTime,
  plural,
  utcMillis
} from '../chat-consistency-format';
import {
  CcEventGroup,
  CcTaggedAnnotation,
  eventGroupChangesText,
  groupOverseerEvents,
  taggedAnnotations
} from '../chat-consistency-events';
import {
  CC_NO_PERIOD_IDS,
  CcPeriod,
  CcPeriodBound,
  CcPeriodIds,
  CcPeriodUnit,
  CcPeriodWindows,
  CcPresetOutcome,
  CcUnitPeriod,
  ccBeforeAfter,
  ccConfirmOnLaterData,
  ccEarliestVsLatest,
  ccIdsFromUnits,
  ccPeriodAssignment,
  ccPeriodUnits,
  ccPeriodWindows,
  ccPeriodsRefusal,
  ccPruneIds,
  ccToggleBound,
  sameIds
} from '../chat-consistency-periods';
import {
  CC_PROTOCOL_V1,
  CC_PROTOCOL_V1_ENDPOINTS,
  CcEndpointReadiness,
  CcPreviewNote,
  ccEndpointReadiness,
  ccIneligibleInPeriods,
  ccMarginText,
  ccPeriodSample,
  ccPreviewNotes,
  ccSampleCountText,
  ccSampleLine,
  ccSegmentNotes
} from '../chat-consistency-readiness';
import { CC_ALL_DATES, CcDateRange, ccDateRangeText, ccRangeBounds } from '../chat-consistency-range';
import { CC_EMPTY_SCOPE, CcRunScope, scopeIsDefault } from '../chat-consistency-scope';
import {
  CcAnalysisRequest,
  CcAnalysisResult,
  CcAnalysisSummary,
  CcAnnotation,
  CcBatteryRunRow,
  CcBatteryTimelinePoint,
  CcComparisonSet,
  CcModelAxis,
  CcProtocolOverrides,
  CcRunRow,
  CcRunSelection,
  CcTimeline,
  CcTimelinePoint
} from '../chat-consistency.models';
import { CcModelBadgesComponent } from '../model-badges/model-badges.component';
import { CcAnalysisPreviewComponent, CcPreviewFact } from './analysis-preview/analysis-preview.component';
import { CcPeriodSummaryComponent } from './period-summary/period-summary.component';
import { CcPeriodUnitsComponent } from './period-units/period-units.component';
import { CcRegradePanelComponent } from './regrade-panel.component';
import { CcReportsStepComponent } from './reports-step.component';
import { CcResultsViewComponent } from './results-view.component';

export { CC_PROTOCOL_V1, CC_PROTOCOL_V1_ENDPOINTS } from '../chat-consistency-readiness';

/** The analysis steps the outer wizard shows through this component, in order. */
export type CcAnalysisStep = 'analyze' | 'results' | 'reports' | 'documents';

/** The Split rule's id: a preset that computes the four bounds, or `custom` (*Manual*) that keeps them. */
export type CcPreset = 'earliest' | 'annotation' | 'event' | 'later' | 'custom';

/** The Split rule select's options, in order. */
export const CC_SPLIT_RULES: readonly { readonly id: CcPreset; readonly label: string }[] = [
  { id: 'earliest', label: 'Earliest vs latest' },
  { id: 'annotation', label: 'Before vs after an annotation' },
  { id: 'event', label: 'Before vs after an Overseer change' },
  { id: 'later', label: 'Confirm on later data' },
  { id: 'custom', label: 'Manual (set on the cards)' }
];

export type CcAnalyzeSidebarTab = 'setup' | 'controls' | 'protocol';
export type CcAnalyzeView = 'periods' | 'preview';

export const CC_ANALYZE_SIDEBAR_TABS: readonly { readonly id: CcAnalyzeSidebarTab; readonly label: string }[] = [
  { id: 'setup', label: 'Setup' },
  { id: 'controls', label: 'Controls' },
  { id: 'protocol', label: 'Protocol' }
];

export const CC_ANALYZE_VIEWS: readonly { readonly id: CcAnalyzeView; readonly label: string }[] = [
  { id: 'periods', label: 'Periods' },
  { id: 'preview', label: 'Preview' }
];

/** The Analyze step's workspace layout, per browser. Read and written in `try/catch`. */
export const CC_ANALYZE_STORAGE_KEY = 'overseer.benchmark.chatConsistency.analyze';

/** The settings sidebar's width, in CSS px, as the Timeline step's: 26 rem by default, 18 rem to 40 rem or half the workspace. */
export const CC_ANALYZE_SIDEBAR_WIDTH_DEFAULT = 416;
export const CC_ANALYZE_SIDEBAR_WIDTH_MIN = 288;
export const CC_ANALYZE_SIDEBAR_WIDTH_MAX = 640;

export interface CcAnalyzeLayout {
  readonly sidebarTab: CcAnalyzeSidebarTab;
  readonly view: CcAnalyzeView;
  readonly sidebarCollapsed: boolean;
  readonly sidebarWidth: number;
  /** *Show run details* on the period cards. */
  readonly details: boolean;
}

export const CC_DEFAULT_ANALYZE_LAYOUT: CcAnalyzeLayout = Object.freeze({
  sidebarTab: 'setup',
  view: 'periods',
  sidebarCollapsed: false,
  sidebarWidth: CC_ANALYZE_SIDEBAR_WIDTH_DEFAULT,
  details: true
});

/** The stored layout, field by field: a missing field, or one of the wrong kind, takes its default. */
export function readStoredAnalyzeLayout(): CcAnalyzeLayout {
  const fallback = CC_DEFAULT_ANALYZE_LAYOUT;
  let record: Record<string, unknown> | null = null;
  try {
    const raw = localStorage.getItem(CC_ANALYZE_STORAGE_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) record = parsed as Record<string, unknown>;
  } catch {
    // Private mode, blocked storage or a damaged value: the defaults.
  }
  if (!record) return fallback;
  const oneOf = <T extends string>(key: string, values: readonly T[], value: T): T =>
    values.includes(record![key] as T) ? record![key] as T : value;
  const flag = (key: string, value: boolean): boolean => (typeof record![key] === 'boolean' ? record![key] as boolean : value);
  const width = record['sidebarWidth'];
  return {
    sidebarTab: oneOf('sidebarTab', CC_ANALYZE_SIDEBAR_TABS.map(tab => tab.id), fallback.sidebarTab),
    view: oneOf('view', CC_ANALYZE_VIEWS.map(view => view.id), fallback.view),
    sidebarCollapsed: flag('sidebarCollapsed', fallback.sidebarCollapsed),
    sidebarWidth: typeof width === 'number' && Number.isFinite(width)
      ? Math.min(CC_ANALYZE_SIDEBAR_WIDTH_MAX, Math.max(CC_ANALYZE_SIDEBAR_WIDTH_MIN, Math.round(width)))
      : fallback.sidebarWidth,
    details: flag('details', fallback.details)
  };
}

function writeStoredAnalyzeLayout(layout: CcAnalyzeLayout): void {
  try {
    localStorage.setItem(CC_ANALYZE_STORAGE_KEY, JSON.stringify({ version: 1, ...layout }));
  } catch {
    // Private mode or blocked storage: the layout still applies for this session.
  }
}

/** Each bound as the polite note names it. */
const BOUND_NOTE_LABELS: Readonly<Record<CcPeriodBound, string>> = {
  baselineFirstId: 'Baseline first run',
  baselineLastId: 'Baseline last run',
  comparisonFirstId: 'Comparison first run',
  comparisonLastId: 'Comparison last run'
};

const NO_POINTS: readonly CcTimelinePoint[] = [];
const NO_BATTERY_POINTS: readonly CcBatteryTimelinePoint[] = [];

/** A saved analysis's units, which name the bounds once they are among the step-1 units. */
interface ResultUnits {
  battery: boolean;
  baseline: readonly number[];
  comparison: readonly number[];
}

/** What the bounds derive, kept until the units, the bounds or the anchor change. */
interface PeriodState {
  units: readonly CcPeriodUnit[];
  ids: CcPeriodIds;
  anchorUtc: string | null;
  battery: boolean;
  refusal: string;
  assignment: ReadonlyMap<number, CcUnitPeriod>;
  /** The eligible units of each period; empty while the bounds are refused. */
  baseline: readonly CcPeriodUnit[];
  comparison: readonly CcPeriodUnit[];
  windows: CcPeriodWindows | null;
}

/**
 * The analysis steps of the Chat Consistency wizard over one subject, without navigation of their
 * own: the outer wizard chooses the step through `step` and draws the step bar, headings and footer.
 * *Analyze* is a workspace: a settings sidebar (Setup: the subject, the name and the Split rule;
 * Controls: the control runs, the common-grader re-grade and pooling; Protocol: Protocol V1 and its
 * overrides) beside the *Periods* view — a summary strip over the step-1 units as cards, on which the
 * four period bounds are set — and the *Preview* view of what the analysis will see. *Results* shows
 * the saved result, *Reports* writes its AI reports, and *Documents* lists them in the Download Center
 * panel. A step's body is mounted on its first visit and afterwards kept, hidden while another step
 * shows, so a re-grade, a report job, a chart attachment and the scroll survive a step change.
 */
@Component({
  selector: 'app-cc-analysis-wizard',
  standalone: true,
  imports: [
    CcAnalysisPreviewComponent,
    CcModelBadgesComponent,
    CcPeriodSummaryComponent,
    CcPeriodUnitsComponent,
    CcRegradePanelComponent,
    CcReportsStepComponent,
    CcResultsViewComponent,
    DownloadCenterPanelComponent,
    InfoTipComponent,
    PaneResizerComponent
  ],
  templateUrl: './analysis-wizard.component.html',
  styleUrls: ['./analysis-wizard.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.is-workspace]': 'step === \'analyze\'',
    '[class.is-fill]': 'step === \'reports\' || step === \'documents\''
  }
})
export class CcAnalysisWizardComponent implements OnInit, OnChanges, OnDestroy {
  private readonly service = inject(AdminChatConsistencyService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() axis: CcModelAxis | null = null;
  @Input() timeline: CcTimeline | null = null;
  /**
   * The runs in the analysis: the step-1 dates narrowed by the step-1 selection; in a battery set, the
   * members of the battery runs in the analysis.
   */
  @Input() rows: readonly CcRunRow[] = [];
  /** Every run of the compared set in the step-1 dates, those left out included. */
  @Input() allRows: readonly CcRunRow[] = [];
  /** The battery or suite compared within; null analyzes the runs one by one. */
  @Input() compareSet: CcComparisonSet | null = null;
  /** In a battery set, the battery runs in the analysis, oldest first. */
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  /** In a battery set, every battery run of the set in the step-1 dates. */
  @Input() allBatteryRows: readonly CcBatteryRunRow[] = [];
  /** The step-1 selection, recorded with the analysis: battery run ids in a battery set. */
  @Input() scope: CcRunScope = CC_EMPTY_SCOPE;
  /** The step-1 dates, recorded with the analysis. */
  @Input() range: CcDateRange = CC_ALL_DATES;
  /** Changes with the step-1 selection, so a new selection chooses the controls again. */
  @Input() scopeKey = '';
  @Input() analyses: readonly CcAnalysisSummary[] = [];
  /** The benchmark-capable configurations the launcher offers: the re-grade's assessors and the report writers. */
  @Input() pickerOptions: readonly ModelPickerOption<SystemAiConfigDto>[] = [];
  @Input() pickerConfigs: readonly SystemAiConfigDto[] = [];
  @Input() pickerEmptyHint: string | null = null;
  /** The step whose body shows. */
  @Input() step: CcAnalysisStep = 'analyze';

  @Output() readonly analysisSaved = new EventEmitter<CcAnalysisResult>();
  @Output() readonly runsChanged = new EventEmitter<void>();
  @Output() readonly repeatSetup = new EventEmitter<number>();
  /** A step asks the outer wizard to show another: the Reports step's *See the documents*. */
  @Output() readonly stepRequested = new EventEmitter<'documents'>();
  /** A period card's run report, by run id. */
  @Output() readonly openRunReport = new EventEmitter<number>();
  /** A period card's battery run report, by battery run id. */
  @Output() readonly openBatteryRunReport = new EventEmitter<number>();
  /** The state the outer wizard reads (errors, blocked reason, analyzing, result) may have changed. */
  @Output() readonly stateChange = new EventEmitter<void>();

  @ViewChild(CcReportsStepComponent) reportsStep?: CcReportsStepComponent;
  @ViewChild(CcPeriodUnitsComponent) periodUnitsList?: CcPeriodUnitsComponent;
  @ViewChild('workspace') private workspaceRef?: ElementRef<HTMLElement>;

  readonly protocolEndpoints = CC_PROTOCOL_V1_ENDPOINTS;
  readonly protocol = CC_PROTOCOL_V1;
  readonly rules = CC_SPLIT_RULES;
  readonly sidebarTabs = CC_ANALYZE_SIDEBAR_TABS;
  readonly views = CC_ANALYZE_VIEWS;
  readonly SIDEBAR_WIDTH_MIN = CC_ANALYZE_SIDEBAR_WIDTH_MIN;
  readonly SIDEBAR_WIDTH_DEFAULT = CC_ANALYZE_SIDEBAR_WIDTH_DEFAULT;

  private readonly stored = readStoredAnalyzeLayout();

  // --- Workspace layout ---

  sidebarTab: CcAnalyzeSidebarTab = this.stored.sidebarTab;
  view: CcAnalyzeView = this.stored.view;
  sidebarCollapsed = this.stored.sidebarCollapsed;
  sidebarWidth = this.stored.sidebarWidth;
  sidebarWidthMax = CC_ANALYZE_SIDEBAR_WIDTH_MAX;
  details = this.stored.details;

  name = '';
  /** The first and last unit of each period, by id. */
  ids: CcPeriodIds = CC_NO_PERIOD_IDS;
  preset: CcPreset = 'custom';
  presetAnnotationId: number | null = null;
  /** The key of the composite event the *Before vs after an Overseer change* rule is around. */
  presetEventGroupKey: string | null = null;
  /** The instant the applied before-and-after rule splits at; null for any other rule. */
  presetAnchorUtc: string | null = null;
  presetNote = '';
  /** What the last bound press or *Split here* did, for the polite note under the view bar. */
  periodNote = '';

  /** The margin overrides as typed, by endpoint id: index points for P1, percent for the others. */
  marginOverrides: Record<string, string> = {};
  alphaOverride = '';

  /** The checked control runs; only those among {@link controlCandidates} are used. */
  readonly controlSelected = new Set<number>();
  /** Bumped whenever `controlSelected` changes, for the memoized preview. */
  private controlsRevision = 0;
  relaxedPooling = false;
  private selectionKey: string | null = null;
  /** A saved analysis's units, until they name the bounds or another choice replaces them. */
  private resultUnits: ResultUnits | null = null;

  analyzing = false;
  analyzeError: string | null = null;
  result: CcAnalysisResult | null = null;

  /** Bumped to list the Documents step's documents again: on entering it, and on a Reports step change. */
  documentsReloadToken = 0;

  private analyzeSub: Subscription | null = null;
  /** The steps whose bodies have been shown, and so stay mounted. */
  private readonly visitedSteps = new Set<CcAnalysisStep>();
  private unitsCache: {
    rows: readonly CcRunRow[]; batteryRows: readonly CcBatteryRunRow[]; battery: boolean; units: CcPeriodUnit[];
  } | null = null;
  private stateCache: PeriodState | null = null;
  private readonly memos = new Map<string, { deps: readonly unknown[]; value: unknown }>();

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['step']) {
      this.visitedSteps.add(this.step);
      if (this.step === 'documents' && changes['step'].previousValue !== 'documents') this.documentsReloadToken++;
    }
    const axisChange = changes['axis'];
    let reset = false;
    if (axisChange && axisChange.previousValue?.key !== this.axis?.key) {
      // A saved result just opened for this subject is kept; any other subject starts over.
      if (!(this.result && this.axis && this.result.subject.key === this.axis.key)) {
        this.resetForSubject();
        reset = true;
      }
    }
    if (reset) return;
    if (changes['rows'] || changes['batteryRows'] || changes['compareSet'] || changes['scopeKey']) {
      this.followUnits();
    } else if (changes['timeline'] && (this.preset === 'annotation' || this.preset === 'event')) {
      this.choosePreset(this.preset);
    }
  }

  /** A battery set is compared: the units are battery runs. */
  get batteryMode(): boolean {
    return this.compareSet?.kind === 'battery';
  }

  /** `run`, or `battery run` in a battery set. */
  get noun(): string {
    return this.batteryMode ? 'battery run' : 'run';
  }

  ngOnDestroy(): void {
    this.analyzeSub?.unsubscribe();
  }

  /** The value of `compute()`, kept while every dependency stays the same (`===`). */
  private memo<T>(name: string, deps: readonly unknown[], compute: () => T): T {
    const hit = this.memos.get(name);
    if (hit && hit.deps.length === deps.length && hit.deps.every((dep, i) => dep === deps[i])) return hit.value as T;
    const value = compute();
    this.memos.set(name, { deps, value });
    return value;
  }

  // --- Steps ---

  /** A step's body is rendered while it shows and, once shown, kept mounted and hidden. */
  isMounted(step: CcAnalysisStep): boolean {
    return step === this.step || this.visitedSteps.has(step);
  }

  /** The Reports step is drawing and uploading report charts; closing the wizard would strand them. */
  get chartsAttaching(): boolean {
    return this.reportsStep?.chartState === 'attaching';
  }

  /** The Documents step's Download Center context: one object per saved analysis; null without one. */
  get documentsContext(): DownloadCenterChatConsistencyContext | null {
    const analysisId = this.result?.analysisId ?? null;
    return this.memo('documentsContext', [analysisId], (): DownloadCenterChatConsistencyContext | null =>
      analysisId === null ? null : { kind: 'chatConsistency', analysisId });
  }

  /** `The documents of analysis #7 — Weekly check. View them here, or select them to download.` */
  get documentsNote(): string {
    const result = this.result;
    if (!result || result.analysisId === null) return '';
    const name = result.name?.trim();
    return `The documents of analysis #${result.analysisId}${name ? ` — ${name}` : ''}. View them here, or select them to download.`;
  }

  /** The Reports step's documents changed: the Documents step lists them again. */
  onReportsDocumentsChanged(): void {
    this.documentsReloadToken++;
    this.cdr.markForCheck();
  }

  /** The Reports step's *See the documents*: the outer wizard shows the Documents step. */
  onDocumentsRequested(): void {
    this.stepRequested.emit('documents');
  }

  /** A document was deleted, or its charts changed, in the Documents step: the Reports step lists its documents again. */
  onDocumentsChanged(): void {
    this.reportsStep?.reloadDocuments();
  }

  /** Whether a report job runs or charts are attached changed: the outer wizard's close guard reads it. */
  onReportsStateChange(): void {
    this.changed();
  }

  get periodsError(): string {
    return this.periodState.refusal;
  }

  get overridesError(): string {
    for (const endpoint of CC_PROTOCOL_V1_ENDPOINTS) {
      const text = (this.marginOverrides[endpoint.id] ?? '').trim();
      if (text === '') continue;
      const value = Number(text);
      if (!Number.isFinite(value) || value <= 0) return `The margin of ${endpoint.id} must be a positive number.`;
    }
    const alpha = this.alphaOverride.trim();
    if (alpha !== '') {
      const value = Number(alpha);
      if (!Number.isFinite(value) || value <= 0 || value >= 0.5) return 'α must lie strictly between 0 and 0.5.';
    }
    return '';
  }

  /** Analyze needs a model, Results a result, and Reports and Documents a saved one. */
  reachable(step: CcAnalysisStep): boolean {
    switch (step) {
      case 'analyze':
        return !!this.axis;
      case 'results':
        return this.result !== null;
      default:
        return this.result !== null && this.result.analysisId !== null;
    }
  }

  /**
   * Loads a saved analysis, for the outer wizard to show on its results. Its units name the bounds,
   * under *Manual*, as soon as they are among the step-1 units.
   */
  showResult(result: CcAnalysisResult): void {
    this.analyzeSub?.unsubscribe();
    this.analyzing = false;
    this.analyzeError = null;
    this.result = result;
    this.name = result.name ?? '';
    this.preset = 'custom';
    this.presetNote = '';
    this.presetAnchorUtc = null;
    this.periodNote = '';
    this.relaxedPooling = result.endpoints.some(endpoint => endpoint.relaxedPooling);
    if (result.unitKind === 'batteryRun' && result.units) {
      const unitsOf = (period: string) => result.units!.filter(unit => unit.period === period).map(unit => unit.unitId);
      this.resultUnits = { battery: true, baseline: unitsOf('baseline'), comparison: unitsOf('comparison') };
    } else {
      this.resultUnits = { battery: false, baseline: result.baseline.runIds, comparison: result.comparison.runIds };
    }
    this.ids = this.idsFromResult(this.resultUnits);
    this.replaceSelection(this.controlSelected, result.controls.controlRunIds);
    this.selectionKey = this.periodKey;
    this.changed();
  }

  /** Marks the view for check and tells the outer wizard its state may have changed. */
  private changed(): void {
    this.cdr.markForCheck();
    this.stateChange.emit();
  }

  // --- Workspace ---

  measureSidebarWidthMax(): void {
    const workspace = this.workspaceRef?.nativeElement.clientWidth ?? 0;
    this.sidebarWidthMax = workspace > 0
      ? Math.max(CC_ANALYZE_SIDEBAR_WIDTH_MIN, Math.min(CC_ANALYZE_SIDEBAR_WIDTH_MAX, Math.floor(workspace / 2)))
      : CC_ANALYZE_SIDEBAR_WIDTH_MAX;
    this.cdr.markForCheck();
  }

  /** Live while dragging. */
  onSidebarWidthChange(width: number): void {
    this.sidebarWidth = width;
    this.cdr.markForCheck();
  }

  onSidebarWidthCommit(width: number): void {
    this.sidebarWidth = width;
    this.persist();
    this.cdr.markForCheck();
  }

  /** Focus stays on the toggle, which sits outside the sidebar and is always rendered. */
  toggleSidebar(): void {
    this.sidebarCollapsed = !this.sidebarCollapsed;
    this.persist();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
  }

  selectSidebarTab(tab: CcAnalyzeSidebarTab): void {
    if (tab === this.sidebarTab) return;
    this.sidebarTab = tab;
    this.persist();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
  }

  /** Left/Right move and wrap, Home/End jump to the ends; focus follows selection. */
  onSidebarTabKeydown(event: KeyboardEvent, index: number): void {
    const next = this.rovingTabIndex(event, index, this.sidebarTabs.length);
    if (next === null) return;
    const tab = this.sidebarTabs[next].id;
    this.selectSidebarTab(tab);
    document.getElementById(`cc-an-side-tab-${tab}`)?.focus();
  }

  selectView(view: CcAnalyzeView): void {
    if (view === this.view) return;
    this.view = view;
    this.persist();
    this.cdr.detectChanges();
    refreshAnchorPositioning();
  }

  onViewTabKeydown(event: KeyboardEvent, index: number): void {
    const next = this.rovingTabIndex(event, index, this.views.length);
    if (next === null) return;
    const view = this.views[next].id;
    this.selectView(view);
    document.getElementById(`cc-an-view-tab-${view}`)?.focus();
  }

  /** The summary strip's readiness link: the Preview view, focus on its panel. */
  openPreview(): void {
    this.selectView('preview');
    document.getElementById('cc-an-view-panel-preview')?.focus({ preventScroll: true });
  }

  /** The summary strip's bound links: the unit's card, scrolled into view, focus on its title. */
  goToUnit(id: number): void {
    this.periodUnitsList?.focusUnit(id);
  }

  onDetailsChange(details: boolean): void {
    this.details = details;
    this.persist();
    this.cdr.markForCheck();
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

  private persist(): void {
    writeStoredAnalyzeLayout({
      sidebarTab: this.sidebarTab,
      view: this.view,
      sidebarCollapsed: this.sidebarCollapsed,
      sidebarWidth: this.sidebarWidth,
      details: this.details
    });
  }

  // --- Subject ---

  /** What the Compared tag reads: `battery`, `suite`, or `all` when the runs are analyzed one by one. */
  get compareKind(): 'battery' | 'suite' | 'all' {
    return this.compareSet?.kind ?? 'all';
  }

  get compareKindText(): string {
    return this.compareKind === 'battery' ? 'Battery' : this.compareKind === 'suite' ? 'Suite' : 'All suites';
  }

  get comparedLabel(): string {
    return this.compareSet?.label ?? 'Runs analyzed one by one';
  }

  /** The step-1 units the periods can use: `2 battery runs from step 1 · each analyzed as one unit, its member runs together`. */
  get comparedUnitsText(): string {
    const count = this.units.length;
    switch (this.compareKind) {
      case 'battery':
        return `${plural(count, 'battery run')} from step 1 · each analyzed as one unit, its member runs together`;
      case 'suite':
        return `${plural(count, 'single-suite run')} from step 1`;
      default:
        return `${plural(count, 'run')} from step 1`;
    }
  }

  // --- Units and periods ---

  /** The step-1 units, ordered by start then id: battery runs in a battery set (complete ones only), else runs. */
  get units(): readonly CcPeriodUnit[] {
    const battery = this.batteryMode;
    const cache = this.unitsCache;
    if (cache && cache.rows === this.rows && cache.batteryRows === this.batteryRows && cache.battery === battery) {
      return cache.units;
    }
    const units = ccPeriodUnits(this.rows, this.batteryRows, battery);
    this.unitsCache = { rows: this.rows, batteryRows: this.batteryRows, battery, units };
    return units;
  }

  private get periodState(): PeriodState {
    const units = this.units;
    const battery = this.batteryMode;
    const cache = this.stateCache;
    if (cache && cache.units === units && cache.ids === this.ids && cache.anchorUtc === this.presetAnchorUtc
      && cache.battery === battery) {
      return cache;
    }
    const refusal = ccPeriodsRefusal(units, this.ids, battery);
    const assignment = ccPeriodAssignment(units, this.ids);
    const state: PeriodState = {
      units,
      ids: this.ids,
      anchorUtc: this.presetAnchorUtc,
      battery,
      refusal,
      assignment,
      baseline: refusal ? [] : units.filter(unit => assignment.get(unit.id) === 'baseline'),
      comparison: refusal ? [] : units.filter(unit => assignment.get(unit.id) === 'comparison'),
      windows: refusal ? null : ccPeriodWindows(units, this.ids, this.presetAnchorUtc)
    };
    this.stateCache = state;
    return state;
  }

  /** The windows the request carries; null while the bounds are refused. */
  get windows(): CcPeriodWindows | null {
    return this.periodState.windows;
  }

  /** Every unit's period, by unit id. */
  get assignment(): ReadonlyMap<number, CcUnitPeriod> {
    return this.periodState.assignment;
  }

  /** The eligible units of a period; empty while the bounds are refused. */
  periodUnits(period: CcPeriod): readonly CcPeriodUnit[] {
    return period === 'baseline' ? this.periodState.baseline : this.periodState.comparison;
  }

  /** The eligible units in neither period; 0 while the bounds are refused. */
  get notUsedCount(): number {
    const state = this.periodState;
    if (state.refusal) return 0;
    return state.units.filter(unit => state.assignment.get(unit.id) === 'notUsed').length;
  }

  /** The timeline's run points, never a fresh empty array. */
  get points(): readonly CcTimelinePoint[] {
    return this.timeline?.points ?? NO_POINTS;
  }

  get batteryPoints(): readonly CcBatteryTimelinePoint[] {
    return this.timeline?.batteryPoints ?? NO_BATTERY_POINTS;
  }

  /** A period's sample as one sentence; see {@link ccSampleLine}. */
  sampleLine(period: CcPeriod): string {
    return ccSampleLine(period, this.periodUnits(period), this.batteryMode);
  }

  /** The bounds of a saved analysis's units among the current units; unset where none is. */
  private idsFromResult(units: ResultUnits): CcPeriodIds {
    if (units.battery !== this.batteryMode) return CC_NO_PERIOD_IDS;
    return ccIdsFromUnits(this.units, units.baseline, units.comparison);
  }

  /**
   * The step-1 units changed: a Split rule is applied again; *Manual* bounds stay while their units
   * do, and a saved analysis's units name them once they arrive.
   */
  private followUnits(): void {
    if (this.preset !== 'custom') {
      this.choosePreset(this.preset);
      return;
    }
    if (this.resultUnits) {
      const ids = this.idsFromResult(this.resultUnits);
      if (!sameIds(ids, this.ids)) {
        this.ids = ids;
        // The saved analysis's controls stay checked.
        this.selectionKey = this.periodKey;
      }
    } else {
      this.ids = ccPruneIds(this.units, this.ids);
    }
    this.periodsChanged();
  }

  /** The bounds or the units may have changed: the controls follow, and the outer wizard hears of it. */
  private periodsChanged(): void {
    this.syncControls();
    this.changed();
  }

  /**
   * A bound pressed on a card: it moves to that unit, or clears when the unit held it; the rule
   * becomes *Manual*, and the polite note says what happened.
   */
  onBoundChange(change: { key: CcPeriodBound; unitId: number }): void {
    const ruleChanged = this.preset !== 'custom';
    this.ids = ccToggleBound(this.ids, change.key, change.unitId);
    this.preset = 'custom';
    this.presetNote = '';
    this.presetAnchorUtc = null;
    this.resultUnits = null;
    const label = BOUND_NOTE_LABELS[change.key];
    const what = this.ids[change.key] === null ? `${label} cleared.` : `${label}: ${this.noun} #${change.unitId}.`;
    this.periodNote = ruleChanged ? `${what} Split rule set to Manual.` : what;
    this.periodsChanged();
  }

  /** *Split here* on a card list marker: the matching before-and-after rule at that anchor. */
  onSplitAt(split: { kind: 'event' | 'annotation'; key: string }): void {
    if (split.kind === 'event') {
      this.presetEventGroupKey = split.key;
      this.choosePreset('event');
      const tag = this.eventGroups.find(group => group.key === split.key)?.tag ?? '';
      this.periodNote = `Split at Overseer change ${tag}. ${this.presetNote}`.trim();
    } else {
      this.presetAnnotationId = Number(split.key);
      this.choosePreset('annotation');
      const tag = this.taggedAnnotations.find(entry => entry.annotation.id === this.presetAnnotationId)?.tag ?? '';
      this.periodNote = `Split at annotation ${tag}. ${this.presetNote}`.trim();
    }
    this.cdr.markForCheck();
  }

  // --- Split rule ---

  /** The annotations the *Before vs after an annotation* rule offers. */
  get annotations(): readonly CcAnnotation[] {
    return this.timeline?.annotations ?? [];
  }

  /** The annotations in time order, tagged `A1`…, as the timeline tags them. */
  get taggedAnnotations(): readonly CcTaggedAnnotation[] {
    return this.memo('tagged', [this.timeline], () => taggedAnnotations(this.annotations));
  }

  /** The timeline's Overseer events grouped into composite events, oldest first; recomputed per timeline. */
  get eventGroups(): readonly CcEventGroup[] {
    const timeline = this.timeline;
    return this.memo('groups', [timeline], () => groupOverseerEvents(timeline?.events ?? [], timeline?.points ?? []));
  }

  /**
   * The most recent saved analysis of this subject, within the same battery or suite while a set is
   * compared: the last look *Confirm on later data* starts after.
   */
  get lastAnalysis(): CcAnalysisSummary | null {
    const key = this.axis?.key;
    if (!key) return null;
    const setKey = this.compareSet?.key ?? null;
    return [...this.analyses]
      .filter(analysis => analysis.subjectModelKey === key && (setKey === null || analysis.comparisonSetKey === setKey))
      .sort((a, b) => utcMillis(b.createdAtUtc) - utcMillis(a.createdAtUtc))[0] ?? null;
  }

  /** Applies a Split rule to the step-1 units; *Manual* keeps the current bounds. */
  choosePreset(preset: CcPreset): void {
    this.preset = preset;
    this.presetNote = '';
    this.presetAnchorUtc = null;
    this.periodNote = '';
    this.resultUnits = null;
    const units = this.units;
    const battery = this.batteryMode;
    if (this.axis) {
      switch (preset) {
        case 'earliest':
          this.applyPreset(ccEarliestVsLatest(units, battery));
          break;
        case 'annotation': {
          const tagged = this.taggedAnnotations;
          const annotation = tagged.find(entry => entry.annotation.id === this.presetAnnotationId)?.annotation ?? tagged[0]?.annotation;
          if (!annotation) {
            this.applyPreset({ ids: CC_NO_PERIOD_IDS, note: 'This model has no annotation in the timeline range.', anchorUtc: null });
            break;
          }
          this.presetAnnotationId = annotation.id;
          this.applyPreset(ccBeforeAfter(units, annotation.atUtc, battery, 'the annotation'));
          break;
        }
        case 'event': {
          const groups = this.eventGroups;
          const group = groups.find(g => g.key === this.presetEventGroupKey) ?? groups[0];
          if (!group) {
            this.applyPreset({ ids: CC_NO_PERIOD_IDS, note: 'No Overseer change was detected in the timeline range.', anchorUtc: null });
            break;
          }
          this.presetEventGroupKey = group.key;
          this.applyPreset(ccBeforeAfter(units, group.atUtc, battery, `the Overseer change ${group.tag}`));
          break;
        }
        case 'later': {
          const last = this.lastAnalysis;
          if (!last) {
            const note = this.compareSet
              ? `This model has no saved analysis of ${this.compareSet.label} yet; there is no earlier look to confirm.`
              : 'This model has no saved analysis yet; there is no earlier look to confirm.';
            this.applyPreset({ ids: CC_NO_PERIOD_IDS, note, anchorUtc: null });
            break;
          }
          this.applyPreset(ccConfirmOnLaterData(units, last, battery));
          break;
        }
        case 'custom':
          this.ids = ccPruneIds(units, this.ids);
          break;
      }
    }
    this.periodsChanged();
  }

  private applyPreset(outcome: CcPresetOutcome): void {
    this.ids = sameIds(outcome.ids, this.ids) ? this.ids : outcome.ids;
    this.presetNote = outcome.note;
    this.presetAnchorUtc = outcome.anchorUtc;
  }

  onRuleChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as CcPreset;
    if (CC_SPLIT_RULES.some(rule => rule.id === value)) this.choosePreset(value);
  }

  onAnnotationPick(event: Event): void {
    this.presetAnnotationId = Number((event.target as HTMLSelectElement).value);
    this.choosePreset('annotation');
  }

  onEventPick(event: Event): void {
    this.presetEventGroupKey = (event.target as HTMLSelectElement).value;
    this.choosePreset('event');
  }

  /**
   * Which units the Split rule spans: `Presets use the runs chosen in step 1: #21 (2026-09-20) to #93
   * (2026-10-05), 15 runs.`, or every run in the dates while step 1 leaves the selection alone; in a
   * battery set `Presets use the battery runs chosen in step 1: #11 (2026-10-08) to #12 (2026-10-08),
   * 2 battery runs.`
   */
  get spanNote(): string {
    const battery = this.batteryMode;
    const units = battery
      ? this.batteryRows.map(row => ({ id: row.batteryRunId, startedAtUtc: row.startedAtUtc }))
      : this.rows.map(row => ({ id: row.runId, startedAtUtc: row.startedAtUtc }));
    if (units.length === 0) return '';
    const noun = battery ? 'battery run' : 'run';
    const ordered = units.sort((a, b) => utcMillis(a.startedAtUtc) - utcMillis(b.startedAtUtc) || a.id - b.id);
    const first = ordered[0];
    const last = ordered[ordered.length - 1];
    const runs = first === last
      ? `#${first.id} (${formatUtcDate(first.startedAtUtc)}), 1 ${noun}`
      : `#${first.id} (${formatUtcDate(first.startedAtUtc)}) to #${last.id} (${formatUtcDate(last.startedAtUtc)}), ${plural(ordered.length, noun)}`;
    const incomplete = battery && this.allBatteryRows.some(row => !row.complete);
    return scopeIsDefault(this.scope) && !incomplete
      ? `Presets use every ${noun} in the dates: ${runs}.`
      : `Presets use the ${noun}s chosen in step 1: ${runs}.`;
  }

  onNameInput(event: Event): void {
    this.name = (event.target as HTMLInputElement).value;
  }

  marginValue(id: string): string {
    return this.marginOverrides[id] ?? '';
  }

  marginText(margin: number, unit: 'index points' | '%'): string {
    return ccMarginText(margin, unit);
  }

  onMarginInput(id: string, event: Event): void {
    this.marginOverrides = { ...this.marginOverrides, [id]: (event.target as HTMLInputElement).value };
    this.changed();
  }

  onAlphaInput(event: Event): void {
    this.alphaOverride = (event.target as HTMLInputElement).value;
    this.changed();
  }

  /** An annotation as the rule's second select lists it: `A1 · 2026-09-20: New snapshot announced`. */
  annotationLabel(entry: CcTaggedAnnotation): string {
    return `${entry.tag} · ${formatUtcDate(entry.annotation.atUtc)}: ${entry.annotation.text}`;
  }

  /** A composite event as the rule lists it: `E2 · 2026-10-04 · Harness 27 → 28 (5 changes)`, counting its kinds. */
  eventGroupOption(group: CcEventGroup): string {
    return `${group.tag} · ${group.day} · ${group.title} (${plural(group.changes.length, 'change')})`;
  }

  /** A composite event's kinds, `System prompt ×3, Tool guides`; the harness is left out when the title names it. */
  eventGroupChanges(group: CcEventGroup): string {
    return eventGroupChangesText(group);
  }

  // --- Controls ---

  private get periodKey(): string {
    const ids = this.ids;
    return `${this.axis?.key}|${ids.baselineFirstId}|${ids.baselineLastId}|${ids.comparisonFirstId}|${ids.comparisonLastId}|${this.scopeKey}`;
  }

  /** Checks every control candidate when the bounds or the step-1 selection changed; true when it did. */
  private syncControls(): boolean {
    const key = this.periodKey;
    if (key === this.selectionKey) return false;
    this.selectionKey = key;
    this.replaceSelection(this.controlSelected, this.controlCandidates);
    return true;
  }

  /**
   * Checks every matched control the first time these bounds reach *Analyze*; the outer wizard
   * calls it on entering that step. A change of the bounds does the same by itself.
   */
  preselectRuns(): void {
    if (this.syncControls()) this.changed();
  }

  /** The units left out in step 1 that started inside either window, ascending: battery run ids in a battery set. */
  get leftOutInPeriods(): number[] {
    return this.memo('leftOut', [this.periodState, this.scope, this.allRows, this.allBatteryRows], () => {
      const windows = this.windows;
      if (this.scope.leftOut.size === 0 || !windows) return [];
      const units = this.batteryMode
        ? this.allBatteryRows.map(row => ({ id: row.batteryRunId, startedAtUtc: row.startedAtUtc }))
        : this.allRows.map(row => ({ id: row.runId, startedAtUtc: row.startedAtUtc }));
      const inside = (at: number, start: string, end: string) => at >= utcMillis(start) && at <= utcMillis(end);
      return units
        .filter(unit => {
          if (!this.scope.leftOut.has(unit.id)) return false;
          const at = utcMillis(unit.startedAtUtc);
          return inside(at, windows.baselineStartUtc, windows.baselineEndUtc)
            || inside(at, windows.comparisonStartUtc, windows.comparisonEndUtc);
        })
        .map(unit => unit.id)
        .sort((a, b) => a - b);
    });
  }

  /** The runs of a period's eligible units: the members of its battery runs in a battery set. */
  periodRows(period: CcPeriod): CcRunRow[] {
    return this.periodUnits(period).flatMap(unit => unit.runs);
  }

  private replaceSelection(set: Set<number>, ids: readonly number[]): void {
    set.clear();
    for (const id of ids) set.add(id);
    this.controlsRevision++;
  }

  toggleControl(runId: number, event: Event): void {
    if ((event.target as HTMLInputElement).checked) this.controlSelected.add(runId); else this.controlSelected.delete(runId);
    this.controlsRevision++;
    this.changed();
  }

  onRelaxedPooling(event: Event): void {
    this.relaxedPooling = (event.target as HTMLInputElement).checked;
    this.changed();
  }

  /** The control runs matched to any run of the eligible units in either period, ascending. */
  get controlCandidates(): number[] {
    return this.memo('candidates', [this.periodState], () => {
      const ids = new Set<number>();
      for (const row of [...this.periodRows('baseline'), ...this.periodRows('comparison')]) {
        for (const id of row.matchedControlRunIds) ids.add(id);
      }
      return [...ids].sort((a, b) => a - b);
    });
  }

  /** The checked control candidates, ascending. */
  get selectedControls(): number[] {
    return this.controlCandidates.filter(id => this.controlSelected.has(id));
  }

  /** The runs a re-grade covers: the runs of both periods (the members of their battery runs) and the checked controls. */
  get regradeRunIds(): number[] {
    return this.memo('regrade', [this.periodState, this.controlsRevision], () => {
      const targets = [...this.periodRows('baseline'), ...this.periodRows('comparison')].map(row => row.runId);
      return [...new Set([...targets, ...this.selectedControls])].sort((a, b) => a - b);
    });
  }

  // --- Preview ---

  /** The composite Overseer events between the baseline's start and the comparison's end. */
  get eventGroupsInSpan(): readonly CcEventGroup[] {
    return this.memo('inSpan', [this.periodState, this.eventGroups], () => {
      const windows = this.windows;
      if (!windows) return [];
      const start = utcMillis(windows.baselineStartUtc);
      const end = utcMillis(windows.comparisonEndUtc);
      return this.eventGroups.filter(group => {
        const at = utcMillis(group.atUtc);
        return at >= start && at <= end;
      });
    });
  }

  /** The timeline's run points by run id. */
  private get pointsById(): ReadonlyMap<number, CcTimelinePoint> {
    return this.memo('pointsById', [this.points], () => new Map(this.points.map(point => [point.runId, point])));
  }

  /** Each primary endpoint's readiness over the two periods; see {@link ccEndpointReadiness}. */
  get endpointReadiness(): readonly CcEndpointReadiness[] {
    const state = this.periodState;
    return this.memo('readiness', [state, this.pointsById], () =>
      ccEndpointReadiness(CC_PROTOCOL_V1_ENDPOINTS, state.baseline, state.comparison, this.pointsById, state.battery));
  }

  /** The preview's notes; each counts once in the Preview tab's badge and the strip's link. */
  get previewNotes(): readonly CcPreviewNote[] {
    const state = this.periodState;
    return this.memo('notes', [state, this.eventGroupsInSpan, this.leftOutInPeriods, this.relaxedPooling], () => ccPreviewNotes({
      battery: state.battery,
      eventGroupsInSpan: this.eventGroupsInSpan,
      segmentNotes: ccSegmentNotes(state.baseline, state.comparison, this.relaxedPooling),
      periodRuns: [...this.periodRows('baseline'), ...this.periodRows('comparison')],
      leftOutInPeriods: this.leftOutInPeriods,
      ineligible: state.refusal ? [] : ccIneligibleInPeriods(state.units, state.ids, state.assignment)
    }));
  }

  get noteCount(): number {
    return this.previewNotes.length;
  }

  /** The Input list of the preview after Model and Compared. */
  get previewFacts(): readonly CcPreviewFact[] {
    return this.memo('facts', [
      this.periodState, this.name, this.preset, this.presetAnnotationId, this.presetEventGroupKey, this.controlsRevision,
      this.relaxedPooling, this.marginOverrides, this.alphaOverride, this.leftOutInPeriods, this.timeline
    ], () => {
      const facts: CcPreviewFact[] = [
        { key: 'name', term: 'Name', value: this.name.trim() || 'Automatic' },
        { key: 'rule', term: 'Split rule', value: this.ruleText },
        { key: 'baseline', term: 'Baseline', ...this.periodFact('baseline') },
        { key: 'comparison', term: 'Comparison', ...this.periodFact('comparison') },
        { key: 'notUsed', term: 'Not used', value: this.notUsedCount > 0 ? plural(this.notUsedCount, this.noun) : 'None' }
      ];
      const leftOut = this.leftOutInPeriods;
      if (leftOut.length > 0) {
        facts.push({ key: 'leftOut', term: 'Left out in step 1', value: leftOut.map(id => `#${id}`).join(', ') });
      }
      const candidates = this.controlCandidates;
      facts.push({
        key: 'controls',
        term: 'Control runs',
        value: candidates.length > 0
          ? `${this.selectedControls.length} of ${plural(candidates.length, 'matched control')} checked`
          : 'None matched — the analysis looks for controls among other models\' runs itself'
      });
      facts.push({ key: 'grading', term: 'Grading', value: this.gradingText });
      facts.push({ key: 'pooling', term: 'Pooling', value: this.relaxedPooling ? 'On — grades capped at Indicated' : 'Off' });
      facts.push({ key: 'protocol', term: 'Protocol', ...this.protocolFact });
      return facts;
    });
  }

  /** The rule as the preview names it, with the anchor of a before-and-after rule. */
  private get ruleText(): string {
    const label = CC_SPLIT_RULES.find(rule => rule.id === this.preset)?.label ?? '';
    if (this.preset === 'annotation') {
      const entry = this.taggedAnnotations.find(e => e.annotation.id === this.presetAnnotationId);
      return entry ? `${label} · ${entry.tag} (${formatUtcDate(entry.annotation.atUtc)})` : label;
    }
    if (this.preset === 'event') {
      const group = this.eventGroups.find(g => g.key === this.presetEventGroupKey);
      return group ? `${label} · ${group.tag} (${group.day})` : label;
    }
    return label;
  }

  /** `#101 → #103`, then the sample and the window while the periods are valid. */
  private periodFact(period: CcPeriod): { value: string; note?: string } {
    const first = period === 'baseline' ? this.ids.baselineFirstId : this.ids.comparisonFirstId;
    const last = period === 'baseline' ? this.ids.baselineLastId : this.ids.comparisonLastId;
    const range = first === null && last === null
      ? 'Not set'
      : `${first === null ? 'not set' : `#${first}`} → ${last === null ? 'not set' : `#${last}`}`;
    const windows = this.windows;
    if (!windows) return { value: range };
    const sample = ccPeriodSample(this.periodUnits(period));
    const start = period === 'baseline' ? windows.baselineStartUtc : windows.comparisonStartUtc;
    const end = period === 'baseline' ? windows.baselineEndUtc : windows.comparisonEndUtc;
    return {
      value: `${range} · ${ccSampleCountText(sample, this.batteryMode)}`,
      note: `Window ${formatUtcDateTime(start).replace(/ UTC$/, '')} to ${formatUtcDateTime(end)}`
    };
  }

  /** `Native grades`, or `Re-graded by Claude Opus assessor: 4 of 6 runs` per common grader. */
  private get gradingText(): string {
    const runs = [...this.periodRows('baseline'), ...this.periodRows('comparison')];
    const byGrader = new Map<string, number>();
    for (const run of runs) {
      for (const display of new Set(run.regradeCoverage.map(coverage => coverage.display))) {
        byGrader.set(display, (byGrader.get(display) ?? 0) + 1);
      }
    }
    if (byGrader.size === 0) return 'Native grades';
    return [...byGrader].map(([display, count]) => `Re-graded by ${display}: ${count} of ${plural(runs.length, 'run')}`).join('; ');
  }

  /** `V1`, or `V1 with overrides: P2 margin 20 %, α 0.1`; an invalid override is named as the note. */
  private get protocolFact(): { value: string; note?: string } {
    const error = this.overridesError;
    if (error) return { value: 'V1', note: error };
    const parts: string[] = [];
    for (const endpoint of CC_PROTOCOL_V1_ENDPOINTS) {
      const text = (this.marginOverrides[endpoint.id] ?? '').trim();
      if (text !== '') parts.push(`${endpoint.id} margin ${ccMarginText(Number(text), endpoint.unit).replace(/^±/, '')}`);
    }
    if (this.alphaOverride.trim() !== '') parts.push(`α ${Number(this.alphaOverride.trim())}`);
    return { value: parts.length > 0 ? `V1 with overrides: ${parts.join(', ')}` : 'V1' };
  }

  get analyzeBlocked(): string {
    if (!this.axis) return 'Choose a model in step 1 first.';
    if (this.periodsError) return this.periodsError;
    if (this.overridesError) return this.overridesError;
    return '';
  }

  /** The request Analyze sends. */
  buildRequest(): CcAnalysisRequest | null {
    const axis = this.axis;
    const windows = this.windows;
    if (!axis || !windows || this.analyzeBlocked) return null;
    const idsOf = (period: CcPeriod) => this.periodUnits(period).map(unit => unit.id).sort((a, b) => a - b);
    const baseline = idsOf('baseline');
    const comparison = idsOf('comparison');
    const request: CcAnalysisRequest = {
      subjectModelKey: axis.key,
      ...windows,
      relaxedPooling: this.relaxedPooling,
      runSelection: this.runSelection()
    };
    // A battery set takes the battery runs and refuses run ids; a suite set and no set take run ids.
    if (this.batteryMode) {
      request.baselineBatteryRunIds = baseline;
      request.comparisonBatteryRunIds = comparison;
    } else {
      request.baselineRunIds = baseline;
      request.comparisonRunIds = comparison;
    }
    if (this.compareSet) request.comparisonSet = { kind: this.compareSet.kind, key: this.compareSet.key };
    const name = this.name.trim();
    if (name) request.name = name;
    // Without matched candidates the server chooses the controls itself.
    if (this.controlCandidates.length > 0) request.controlRunIds = this.selectedControls;
    const overrides = this.protocolOverrides();
    if (overrides) request.protocolOverrides = overrides;
    return request;
  }

  /**
   * The step-1 selection as the analysis records it; sent with every request, the default one
   * included. In a battery set the marks and left-out ids are battery runs' and go in the battery fields.
   */
  private runSelection(): CcRunSelection {
    const bounds = ccRangeBounds(this.range);
    const leftOut = [...this.scope.leftOut].sort((a, b) => a - b);
    const selection: CcRunSelection = {
      rangeLabel: ccDateRangeText(this.range),
      rangeFromUtc: bounds.fromUtc,
      rangeToUtc: bounds.toUtc,
      firstRunId: this.batteryMode ? null : this.scope.firstRunId,
      lastRunId: this.batteryMode ? null : this.scope.lastRunId,
      leftOutRunIds: this.batteryMode ? [] : leftOut
    };
    if (this.batteryMode) {
      selection.firstBatteryRunId = this.scope.firstRunId;
      selection.lastBatteryRunId = this.scope.lastRunId;
      selection.leftOutBatteryRunIds = leftOut;
    }
    return selection;
  }

  private protocolOverrides(): CcProtocolOverrides | null {
    const margins: Record<string, number> = {};
    for (const endpoint of CC_PROTOCOL_V1_ENDPOINTS) {
      const text = (this.marginOverrides[endpoint.id] ?? '').trim();
      if (text === '') continue;
      const value = Number(text);
      margins[endpoint.id] = endpoint.unit === '%' ? value / 100 : value;
    }
    const overrides: CcProtocolOverrides = {};
    if (Object.keys(margins).length > 0) overrides.margins = margins;
    if (this.alphaOverride.trim() !== '') overrides.alpha = Number(this.alphaOverride.trim());
    return Object.keys(overrides).length > 0 ? overrides : null;
  }

  analyze(): void {
    const request = this.buildRequest();
    if (!request || this.analyzing) return;
    this.analyzing = true;
    this.analyzeError = null;
    this.changed();
    this.analyzeSub?.unsubscribe();
    this.analyzeSub = this.service.analyze(request).subscribe({
      next: result => {
        this.analyzing = false;
        this.result = result;
        this.changed();
        // The outer wizard moves to Results on this.
        this.analysisSaved.emit(result);
      },
      error: err => {
        this.analyzing = false;
        this.analyzeError = ccErrorText(err, 'The analysis could not be run.');
        this.changed();
      }
    });
  }

  /** Abandons the request in flight; the server stops on the client's abort. */
  stopAnalyze(): void {
    this.analyzeSub?.unsubscribe();
    this.analyzeSub = null;
    this.analyzing = false;
    this.changed();
  }

  onRegradeFinished(): void {
    this.runsChanged.emit();
  }

  // --- Reset ---

  private resetForSubject(): void {
    this.analyzeSub?.unsubscribe();
    this.analyzing = false;
    this.analyzeError = null;
    this.result = null;
    this.name = '';
    this.presetAnnotationId = null;
    this.presetEventGroupKey = null;
    this.marginOverrides = {};
    this.alphaOverride = '';
    this.relaxedPooling = false;
    this.controlSelected.clear();
    this.controlsRevision++;
    this.selectionKey = null;
    this.resultUnits = null;
    this.ids = CC_NO_PERIOD_IDS;
    this.choosePreset(this.axis ? 'earliest' : 'custom');
  }
}
