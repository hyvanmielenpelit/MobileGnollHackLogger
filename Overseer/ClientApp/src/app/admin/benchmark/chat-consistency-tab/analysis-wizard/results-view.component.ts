import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';

import { CcFigure, analysisBands, buildCcFigure, prefersReducedMotion } from '../chat-consistency-charts';
import { CcEventDay, buildEventDays, groupOverseerEvents, servedModelChanges } from '../chat-consistency-events';
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
  CcEndpointResult,
  CcNextRun,
  CcTimelinePoint
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

/** The figures the results draw over the analysis's runs. */
const RESULT_FIGURES = ['quality', 'ttfat', 'rate', 'work', 'cost', 'timeline'] as const;

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

  @Output() readonly repeatSetup = new EventEmitter<number>();

  readonly groups = CC_ATTRIBUTION_GROUPS;
  /** The CSS height of each chart box. */
  readonly figureBoxHeight = 352;
  figures: CcFigure[] = [];
  /** The composite events, annotations and served-model changes of the analysis, by day, tagged as on the charts. */
  eventDays: CcEventDay[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['result'] || changes['points']) {
      const ids = new Set([...this.result.baseline.runIds, ...this.result.comparison.runIds]);
      const points = this.points.filter(point => ids.has(point.runId));
      const input = {
        points,
        events: this.result.events,
        annotations: this.result.annotations,
        bands: analysisBands(this.result.baseline, this.result.comparison)
      };
      const options = { reducedMotion: prefersReducedMotion() };
      this.figures = RESULT_FIGURES.map(key => buildCcFigure(key, input, options));
      this.eventDays = buildEventDays(
        groupOverseerEvents(this.result.events, points), this.result.annotations, servedModelChanges(points));
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
