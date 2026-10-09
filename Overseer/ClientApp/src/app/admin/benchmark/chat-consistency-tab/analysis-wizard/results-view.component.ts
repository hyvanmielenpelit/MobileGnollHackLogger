import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';

import { CcFigure, CcFigureInput, analysisBands, analysisChartPoints, buildCcFigure, prefersReducedMotion } from '../chat-consistency-charts';
import { CcEventDay, CcEventGroup, buildEventDays, groupOverseerEvents, servedModelChanges } from '../chat-consistency-events';
import {
  endpointEstimateText,
  endpointMdeText,
  formatUtcDate,
  formatUtcDateTime,
  gradeText,
  verdictText
} from '../chat-consistency-format';
import {
  CcAnalysisResult,
  CcAttributionResult,
  CcBatteryTimelinePoint,
  CcEndpointResult,
  CcNextRun,
  CcRunSelectionView,
  CcTimelinePoint,
  CcUnanalyzedReason
} from '../chat-consistency.models';
import { CcEventListComponent } from '../event-list/cc-event-list.component';
import { CcChartFigureComponent } from '../timeline-workspace/cc-chart-figure.component';

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

/** The figures the results draw over the analysis's units. */
const RESULT_FIGURES = ['quality', 'ttfat', 'rate', 'work', 'tools', 'cost', 'timeline'] as const;

/** The notes of one verdict table row: legacy data and proxy, common grader, pooling, sample. */
export function endpointNotes(endpoint: CcEndpointResult): string[] {
  const notes: string[] = [];
  if (!endpoint.computed && endpoint.notComputedReason) notes.push(endpoint.notComputedReason);
  if (endpoint.legacyProxy) notes.push('Measured with the legacy proxy (model time per answer), not telemetry.');
  else if (endpoint.usesLegacyData) notes.push('Some compared runs have no call telemetry.');
  if (endpoint.commonGrader) notes.push('Graded by a common grader.');
  else if (endpoint.id === 'P1' && endpoint.computed) notes.push('Native grades; no common grader covers every run.');
  if (endpoint.relaxedPooling) notes.push('Pooled across a measurement segment boundary.');
  if (!endpoint.minimumSampleMet && endpoint.minimumSampleDetail) notes.push(`Below the minimum sample: ${endpoint.minimumSampleDetail}`);
  if (endpoint.minimumDetectableEffectNote) notes.push(endpoint.minimumDetectableEffectNote);
  for (const reason of endpoint.gradeReasons) notes.push(reason);
  return notes;
}

/**
 * The Results step of the analysis: the verdict on the chat first, then the verdict table, the
 * attribution cards by side, the next runs that would resolve what is open, the charts over the
 * analysis's runs in one column, the events in the analyzed span, the limitations and data quality,
 * and the analysis's identity.
 */
@Component({
  selector: 'app-cc-results-view',
  standalone: true,
  imports: [CcChartFigureComponent, CcEventListComponent],
  templateUrl: './results-view.component.html',
  styleUrls: ['./results-view.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcResultsViewComponent implements OnChanges {
  @Input({ required: true }) result!: CcAnalysisResult;
  /** The subject's timeline points; the charts keep the analysis's runs. */
  @Input() points: readonly CcTimelinePoint[] = [];
  /** The timeline's battery points; a battery analysis's charts keep its battery runs. */
  @Input() batteryPoints: readonly CcBatteryTimelinePoint[] = [];
  /** The timeline's composite events, whose E numbers the results reuse. */
  @Input() eventNumbering: readonly CcEventGroup[] = [];

  @Output() readonly repeatSetup = new EventEmitter<number>();

  readonly groups = CC_ATTRIBUTION_GROUPS;
  /** The CSS height of each chart box. */
  readonly figureBoxHeight = 352;
  figures: CcFigure[] = [];
  /** The composite events, annotations and served-model changes of the analysis, by day, tagged as on the charts. */
  eventDays: CcEventDay[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['result'] || changes['points'] || changes['batteryPoints'] || changes['eventNumbering']) {
      const { points, unitKind } = analysisChartPoints(this.result, this.points, this.batteryPoints);
      // The analyzed units are drawn; every timeline point serves the events' harness lookup.
      const input: CcFigureInput = {
        points,
        unitKind,
        events: this.result.events,
        annotations: this.result.annotations,
        bands: analysisBands(this.result.baseline, this.result.comparison),
        harnessPoints: this.points,
        eventNumbering: this.eventNumbering
      };
      const options = { reducedMotion: prefersReducedMotion() };
      this.figures = RESULT_FIGURES.map(key => buildCcFigure(key, input, options));
      this.eventDays = buildEventDays(
        groupOverseerEvents(this.result.events, this.points, this.eventNumbering),
        this.result.annotations, servedModelChanges(points));
    }
  }

  attributionsOf(side: string): CcAttributionResult[] {
    return this.result.attribution.attributions.filter(attribution => attribution.side === side);
  }

  estimate(endpoint: CcEndpointResult): string {
    return endpointEstimateText(endpoint);
  }

  mde(endpoint: CcEndpointResult): string {
    return endpointMdeText(endpoint);
  }

  verdict(endpoint: CcEndpointResult): string {
    return endpoint.computed ? verdictText(endpoint.verdict, endpoint.verdictLabel) : 'Not computable';
  }

  grade(value: string): string {
    return gradeText(value);
  }

  notes(endpoint: CcEndpointResult): string[] {
    return endpointNotes(endpoint);
  }

  day(value: string): string {
    return formatUtcDate(value);
  }

  dateTime(value: string | null): string {
    return formatUtcDateTime(value);
  }

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

  nextRunTitle(next: CcNextRun): string {
    const kinds: Record<string, string> = {
      checkpoint: 'Another run of the model', control: 'A control run', stratum: 'A run at another time of day', regrade: 'A re-grade'
    };
    return kinds[next.kind] ?? next.kind;
  }

  figureId(figure: CcFigure): string {
    return `cc-res-fig-${figure.key}`;
  }
}
