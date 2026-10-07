import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild
} from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import type { ChartConfiguration, ChartType, Plugin } from 'chart.js';

import { CcChartMarker, CcFigure, CcMarkerKind } from '../chat-consistency-charts';

/** A run of marker tags of one kind with consecutive numbers: `E1`–`E4`, or `E7` alone. */
export interface CcMarkerTagRun {
  first: string;
  last: string | null;
}

/** The markers of one kind, as the figure's one-line summary lists them. */
export interface CcMarkerSummaryGroup {
  kind: CcMarkerKind;
  runs: CcMarkerTagRun[];
  /** `Overseer changes`, `annotation`, `served-model change`. */
  noun: string;
}

const MARKER_KIND_ORDER: readonly CcMarkerKind[] = ['event', 'annotation', 'served'];

const MARKER_NOUNS: Readonly<Record<CcMarkerKind, readonly [string, string]>> = {
  event: ['Overseer change', 'Overseer changes'],
  annotation: ['annotation', 'annotations'],
  served: ['served-model change', 'served-model changes']
};

/** The number of a tag such as `E12`; NaN when it carries none. */
function tagNumber(tag: string): number {
  const match = /(\d+)$/.exec(tag);
  return match ? Number(match[1]) : Number.NaN;
}

/**
 * The markers grouped by kind in the order events, annotations, served-model changes; within a
 * kind, the tags in number order folded into runs of consecutive numbers.
 */
export function markerSummary(markers: readonly CcChartMarker[]): CcMarkerSummaryGroup[] {
  const groups: CcMarkerSummaryGroup[] = [];
  for (const kind of MARKER_KIND_ORDER) {
    const tags = markers
      .filter(marker => marker.kind === kind)
      .map(marker => ({ tag: marker.tag, n: tagNumber(marker.tag) }))
      .sort((a, b) => (Number.isFinite(a.n) && Number.isFinite(b.n) ? a.n - b.n : 0));
    if (tags.length === 0) continue;
    const runs: CcMarkerTagRun[] = [];
    let start = tags[0];
    let end = tags[0];
    for (const entry of tags.slice(1)) {
      if (Number.isFinite(entry.n) && entry.n === end.n + 1) {
        end = entry;
      } else {
        runs.push({ first: start.tag, last: end === start ? null : end.tag });
        start = entry;
        end = entry;
      }
    }
    runs.push({ first: start.tag, last: end === start ? null : end.tag });
    const [singular, plural] = MARKER_NOUNS[kind];
    groups.push({ kind, runs, noun: tags.length === 1 ? singular : plural });
  }
  return groups;
}

/** One piece of the marker summary line: text, or a tag drawn as a `.cc-marker-tag` pill. */
export interface CcMarkerSummaryPiece {
  text: string;
  className: string | null;
}

/**
 * The marker summary line as pieces: `Markers: E1–E4 (Overseer changes), A1 (annotation), S1
 * (served-model change)`, each tag a pill of its kind.
 */
export function markerSummaryPieces(groups: readonly CcMarkerSummaryGroup[]): CcMarkerSummaryPiece[] {
  const pieces: CcMarkerSummaryPiece[] = [{ text: 'Markers:', className: 'cc-figure-markers-lead' }];
  groups.forEach((group, g) => {
    const tagClass = `cc-marker-tag is-${group.kind}`;
    pieces.push({ text: ' ', className: null });
    group.runs.forEach((run, r) => {
      if (r > 0) pieces.push({ text: ', ', className: null });
      pieces.push({ text: run.first, className: tagClass });
      if (run.last !== null) {
        pieces.push({ text: '–', className: null });
        pieces.push({ text: run.last, className: tagClass });
      }
    });
    pieces.push({ text: ` (${group.noun})${g < groups.length - 1 ? ',' : ''}`, className: 'cc-figure-marker-noun' });
  });
  return pieces;
}

/**
 * One Chat Consistency chart as a `<figure>`: the title and the takeaway sentence as its caption,
 * the chart (a canvas named by the takeaway and described by a visually hidden list of its
 * markers) in a box of the size the host chooses, a one-line summary of the markers, and a
 * *Show data* disclosure holding the same numbers as a real table.
 */
@Component({
  selector: 'app-cc-chart-figure',
  standalone: true,
  imports: [BaseChartDirective],
  templateUrl: './cc-chart-figure.component.html',
  styleUrls: ['./cc-chart-figure.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcChartFigureComponent implements OnChanges, AfterViewChecked {
  @Input({ required: true }) figure!: CcFigure;
  /** Unique in the document; the caption's, the marker list's and the table's ids derive from it. */
  @Input({ required: true }) figureId = '';
  /** The chart box's width in CSS px; null fills the container. */
  @Input() boxWidth: number | null = null;
  /** The chart box's height in CSS px (22 rem at 16 px). */
  @Input() boxHeight = 352;
  /** Chart.js `devicePixelRatio`; null uses the browser's. */
  @Input() deviceRatio: number | null = null;
  /** False keeps the sized box and draws no canvas. */
  @Input() render = true;

  /** Asks the host to show the event list; the button is shown only while a host listens. */
  @Output() readonly showEvents = new EventEmitter<void>();

  @ViewChild(BaseChartDirective) private chartDirective?: BaseChartDirective;

  // The figure's line configuration, erased to the directive's default chart typing.
  chartType: ChartType = 'line';
  chartData: ChartConfiguration['data'] | null = null;
  chartOptions: ChartConfiguration['options'] = {};
  chartPlugins: Plugin[] = [];
  markerPieces: CcMarkerSummaryPiece[] = [];

  /** Chart.js applies a new device ratio only on a resize, so a ratio change asks for one. */
  private ratioChanged = false;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['figure'] || changes['deviceRatio']) {
      const config = this.figure?.config ?? null;
      this.chartData = config ? (config.data as unknown as ChartConfiguration['data']) : null;
      this.chartOptions = config
        ? ({ ...config.options, devicePixelRatio: this.deviceRatio ?? undefined } as unknown as ChartConfiguration['options'])
        : {};
      this.chartPlugins = config ? (config.plugins as unknown as Plugin[]) : [];
    }
    if (changes['figure']) {
      this.markerPieces = markerSummaryPieces(markerSummary(this.figure?.markers ?? []));
    }
    if (changes['deviceRatio'] && !changes['deviceRatio'].firstChange) {
      this.ratioChanged = true;
    }
  }

  ngAfterViewChecked(): void {
    if (!this.ratioChanged) return;
    this.ratioChanged = false;
    this.chartDirective?.chart?.resize();
  }

  get markersId(): string {
    return `${this.figureId}-markers`;
  }
}
