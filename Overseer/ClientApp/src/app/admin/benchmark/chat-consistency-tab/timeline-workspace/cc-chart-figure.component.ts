import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  ViewChild
} from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import type { ChartConfiguration, ChartType, Plugin } from 'chart.js';

import { CcChartMarker, CcDataListItem, CcFigure, CcFigureTable, CcMarkerKind } from '../chat-consistency-charts';
import { plural } from '../chat-consistency-format';

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

/** One field of a data card: a table column's label and the row's value in it. */
export interface CcDataField {
  label: string;
  value: string;
  /** The value as a list, one item per line; null for a plain value. */
  items: readonly CcDataListItem[] | null;
  /** Spans the whole card row: a list, a note or a long value. */
  wide: boolean;
}

/** One row of a figure's table as a card: `Battery run #11`, its start, and every other column. */
export interface CcDataCard {
  id: string;
  title: string;
  started: string;
  fields: CcDataField[];
}

/** The columns whose values always take a whole card row. */
const WIDE_COLUMNS: ReadonlySet<string> = new Set(['Member runs', 'Note', 'In the analysis', 'Served model']);
/** A value longer than this takes a whole card row. */
const WIDE_VALUE_LENGTH = 32;

/**
 * The table's rows as cards, ids derived from `idPrefix`: column 0 and its cell make the title,
 * column 1 the start, and every other column a field; an empty cell reads `—`, and an empty *Note*
 * is left out. A column the table also carries as lists (`CcFigureTable.lists`) gives its field the
 * row's items, unless that row's list is empty.
 */
export function ccDataCards(table: CcFigureTable, idPrefix: string): CcDataCard[] {
  const [unit = 'Run', , ...labels] = table.columns;
  return table.rows.map((row, r) => ({
    id: `${idPrefix}-row-${r}`,
    title: `${unit} ${row[0] ?? ''}`.trim(),
    started: row[1] ?? '',
    fields: labels
      .map((label, i) => ({ label, value: (row[i + 2] ?? '').trim() }))
      .filter(field => !(field.label === 'Note' && field.value === ''))
      .map(field => {
        const value = field.value === '' ? '—' : field.value;
        const list = table.lists?.[field.label]?.[r];
        const items = list && list.length > 0 ? list : null;
        return {
          label: field.label, value, items,
          wide: items !== null || WIDE_COLUMNS.has(field.label) || value.length > WIDE_VALUE_LENGTH
        };
      })
  }));
}

/** A chart composed as its download (`cc-figure-compose.ts`), shown as a bitmap in a box of whole CSS px. */
export interface CcComposedFigure {
  /** The composed bitmap; null while the first composition is pending, and for a refused size. */
  readonly canvas: HTMLCanvasElement | null;
  readonly cssWidth: number;
  readonly cssHeight: number;
  /** The badges, the Better direction and the notes, read after the alt text. */
  readonly summary: string;
  /** Why the chart size is refused, shown in the image's place; empty otherwise. */
  readonly refusal: string;
  /** The ground is transparent, so the page shows the preview backdrop behind the bitmap. */
  readonly transparent: boolean;
}

/**
 * One Chat Consistency chart as a `<figure>`: the title as its caption, the chart in a box of the size
 * the host chooses, the takeaway sentence under it, a footer row with the actions the host projects
 * (`[ccFigureActions]`), and a *Show data* disclosure holding the same numbers as a list of data rows.
 *
 * The chart is a live Chart.js canvas named by the takeaway, described by a visually hidden list of its
 * markers and summarized on the footer row; or, given `composed`, the composed bitmap of its download,
 * whose image carries the heading and the marker key, and which reacts to no pointer.
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
  /** The chart's header band shows the title, so the caption is visually hidden. */
  @Input() titleInChart = false;
  /** The data cards' heading level, one under the host's section heading. */
  @Input() dataHeadingLevel: 5 | 6 = 6;
  /** The composed bitmap in place of the live chart; null draws the live chart. */
  @Input() composed: CcComposedFigure | null = null;

  /** Asks the host to show the event list; the button is shown only while a host listens. */
  @Output() readonly showEvents = new EventEmitter<void>();

  @ViewChild(BaseChartDirective) private chartDirective?: BaseChartDirective;
  @ViewChild('bitmap') private imageRef?: ElementRef<HTMLCanvasElement>;

  /** The bitmap last drawn, and the canvas it was drawn into. */
  private painted: { source: HTMLCanvasElement; target: HTMLCanvasElement } | null = null;

  // The figure's line configuration, erased to the directive's default chart typing.
  chartType: ChartType = 'line';
  chartData: ChartConfiguration['data'] | null = null;
  chartOptions: ChartConfiguration['options'] = {};
  chartPlugins: Plugin[] = [];
  markerPieces: CcMarkerSummaryPiece[] = [];
  dataCards: CcDataCard[] = [];

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
    if (changes['figure'] || changes['figureId']) {
      this.dataCards = this.figure ? ccDataCards(this.figure.table, this.figureId) : [];
    }
    if (changes['deviceRatio'] && !changes['deviceRatio'].firstChange) {
      this.ratioChanged = true;
    }
  }

  ngAfterViewChecked(): void {
    this.paintComposed();
    if (!this.ratioChanged) return;
    this.ratioChanged = false;
    this.chartDirective?.chart?.resize();
  }

  /** Copies the composed bitmap into the image canvas at its own pixel size; the CSS box scales it. */
  private paintComposed(): void {
    const source = this.composed?.canvas ?? null;
    const target = this.imageRef?.nativeElement ?? null;
    if (!source || !target) {
      this.painted = null;
      return;
    }
    if (this.painted?.source === source && this.painted.target === target) return;
    target.width = source.width;
    target.height = source.height;
    target.getContext('2d')?.drawImage(source, 0, 0);
    this.painted = { source, target };
  }

  get markersId(): string {
    return `${this.figureId}-markers`;
  }

  /** The figure's content width: the composed image's, else the live chart box's; null fills the container. */
  get figureWidth(): number | null {
    return this.composed ? this.composed.cssWidth : this.boxWidth;
  }

  /** The composed image's accessible name: the alt text, then the badges and notes the image carries. */
  get composedLabel(): string {
    const summary = this.composed?.summary ?? '';
    return summary ? `${this.figure.altText} ${summary}` : this.figure.altText;
  }

  /** `run` or `battery run`: what a data card is, from the table's first column. */
  get unitNoun(): string {
    return (this.figure?.table.columns[0] ?? 'Run').toLowerCase();
  }

  /** The *Show data* summary's count: `2 battery runs`, `6 runs`. */
  get dataSummary(): string {
    return plural(this.dataCards.length, this.unitNoun);
  }
}
