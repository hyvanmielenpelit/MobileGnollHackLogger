/**
 * Turns the comparison table into a file, in eight formats, or into a clipboard payload.
 *
 * Pure functions over one model: no Angular, no component state, no `TableState`, so the whole
 * module unit-tests without a fixture and the component keeps only the wiring. The only DOM it
 * touches is the canvas the table image is measured and drawn on.
 *
 * Five things here are load-bearing rather than incidental:
 *
 * 1. **Machine formats carry `raw`, human formats carry `text`.** A spreadsheet cell holding the
 *    string `"$0.0432"` cannot be summed, sorted or charted, and a Markdown cell holding
 *    `0.043200000000000002` is unreadable. Every cell therefore carries both, and each encoder
 *    takes the one its destination can use.
 * 2. **The human text is the on-screen text.** The formatters below are the ones the view renders
 *    with, so a pasted table and the screen it was taken from cannot disagree about what an
 *    absent measure looks like.
 * 3. **CSV and TSV guard against formula injection.** A cell beginning `=`, `+`, `-`, `@`, tab or
 *    carriage return is a formula to Excel, Calc and Sheets the moment the file is opened, so it
 *    is prefixed with an apostrophe (OWASP). XLSX needs no guard: its cells are typed, and a
 *    `String` cell is never evaluated.
 * 4. **The provenance travels with the numbers.** The suite, the pricing basis, the reference
 *    condition and the time the comparison was computed are in every format — a second sheet, a
 *    caption, a JSON object, a subtitle band — because a table of costs with no pricing basis on
 *    it is a table of numbers that cannot be checked.
 * 5. **The table image is refused rather than cropped.** Its size is either the table's own
 *    (*Fit the table*) or an exact box the table is laid out to fill; a box the table cannot fit is
 *    refused with the width or height that would fit it. The measurement, the refusal and the
 *    drawing read one layout, so they cannot disagree.
 */

import type { BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import { showReasoningBadge } from '../../../utils/model-badge-format.util';
import {
  FIGURE_EXPORT_MAX_DIMENSION,
  bitmapRefusal,
  drawFigureBorder,
  encodeFigureImage,
  paintFigureBackground,
  wrapText
} from './figure-export';
import type { FigureExportResult, WebpQuality } from './figure-export';
import { DEFAULT_TABLE_IMAGE_STYLE } from './figure-style';
import type { TableImageStyle } from './figure-style';
import { resolveFigureTheme } from './figure-theme';
import type { ResolvedFigureTheme } from './figure-theme';

/** The eight offered formats. Each id is also the file extension it is written under. */
export type TableExportFormat = 'xlsx' | 'csv' | 'tsv' | 'md' | 'json' | 'html' | 'png' | 'webp';

/** The Download tab's Table format: the text and spreadsheet formats, or the image in the shared image format. */
export type TableFileFormat = 'xlsx' | 'csv' | 'tsv' | 'md' | 'json' | 'html' | 'image';

/**
 * How large the table image is. `fit` is the table's natural size at `density`; `box` is exactly
 * `widthPx × heightPx` requested px at `density`, with one layout px drawn as `textScale` requested px.
 */
export type TableImageSize =
  | { readonly mode: 'fit'; readonly density: number }
  | { readonly mode: 'box'; readonly widthPx: number; readonly heightPx: number; readonly density: number; readonly textScale: number };

/** What the table image is drawn with. Every field absent reproduces the dark, fit, 2×, banded image. */
export interface TableImageOptions {
  readonly theme?: ResolvedFigureTheme;
  readonly tableStyle?: TableImageStyle;
  readonly size?: TableImageSize;
}

/** One resolved table image: the box it is laid out in and the bitmap it is written at. */
export interface TableImageLayout {
  /** The layout box, in layout px. */
  readonly layoutWidth: number;
  readonly layoutHeight: number;
  /** Device px per layout px in the written image: `textScale × density` in box mode, the density in fit mode. */
  readonly scale: number;
  /** The written bitmap, authoritative. */
  readonly pixelWidth: number;
  readonly pixelHeight: number;
  readonly columnCount: number;
  readonly rowCount: number;
}

/**
 * What a column holds, which is what decides its number format, its alignment and whether the
 * formula guard may touch it. `integer` and `ms` are both whole numbers; they are separate because
 * a millisecond column reads in thousands and a count does not.
 */
export type ComparisonTableColumnKind =
  'text' | 'integer' | 'number' | 'money' | 'ms' | 'boolean';

export interface ComparisonTableColumn {
  readonly key: string;
  readonly header: string;
  readonly kind: ComparisonTableColumnKind;
}

/** One cell, in both of the shapes an encoder can want. `raw` is null for an absent measure. */
export interface ComparisonTableCell {
  readonly raw: string | number | boolean | null;
  /** Exactly what the on-screen table prints, `—` included. */
  readonly text: string;
}

export interface ComparisonTableRow {
  readonly cells: Record<string, ComparisonTableCell>;
}

/** Where these numbers came from, in the form every encoder writes it out. */
export interface ComparisonTableProvenance {
  readonly suite: string;
  readonly pricingBasis: string;
  /** Twelve hex characters of the baseline's must-match signature, or empty where none was reached. */
  readonly conditionSignature: string;
  readonly computedAt: string;
  /** `4 of 5 entries charted`. */
  readonly plottedOfTotal: string;
  readonly notices: readonly string[];
}

/** The whole export, once. Every encoder is a pure function of this and nothing else. */
export interface ComparisonTableModel {
  readonly columns: readonly ComparisonTableColumn[];
  readonly rows: readonly ComparisonTableRow[];
  readonly provenance: ComparisonTableProvenance;
}

/** What one encoder produced, including the format actually written. */
export interface TableExportResult {
  readonly blob: Blob;
  /** The requested format, or `'png'` where the browser could not encode WebP. */
  readonly format: TableExportFormat;
  readonly fellBackToPng: boolean;
}

/** The media type an `.xlsx` is served and saved under. */
export const XLSX_MEDIA_TYPE =
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/**
 * The columns, in reading order: identity first, then the comparability verdict, then the three
 * measured axes with their uncertainty beside them, then everything that qualifies the verdict.
 *
 * Twenty-nine of them — every field the on-screen table renders plus the configuration keys it
 * shows only in a tooltip, because an exported table is read away from the tooltips.
 */
export const COMPARISON_TABLE_COLUMNS: readonly ComparisonTableColumn[] = [
  { key: 'label', header: 'Model', kind: 'text' },
  { key: 'source', header: 'Source', kind: 'text' },
  { key: 'provider', header: 'Provider', kind: 'text' },
  { key: 'modelId', header: 'Model id', kind: 'text' },
  { key: 'thinkingLevel', header: 'Thinking level', kind: 'text' },
  { key: 'parallelMode', header: 'Parallel mode', kind: 'text' },
  { key: 'runCount', header: 'R', kind: 'integer' },
  { key: 'state', header: 'State', kind: 'text' },
  { key: 'intelligenceIndex', header: 'Intelligence Index', kind: 'number' },
  { key: 'intervalHalfWidth', header: '±', kind: 'number' },
  { key: 'intervalBasis', header: 'Interval basis', kind: 'text' },
  { key: 'modelTimeMeanMs', header: 'Model time mean ms', kind: 'ms' },
  { key: 'totalModelTimeMs', header: 'Suite total ms', kind: 'ms' },
  { key: 'ttftP50Ms', header: 'TTFT P50 ms', kind: 'ms' },
  { key: 'ttftP90Ms', header: 'TTFT P90 ms', kind: 'ms' },
  { key: 'speedIndex', header: 'Speed Index', kind: 'number' },
  { key: 'speedIndexSaturated', header: 'Saturated', kind: 'boolean' },
  { key: 'costPerQuestion', header: 'Candidate $/question', kind: 'money' },
  { key: 'costPerRun', header: 'Candidate $/run', kind: 'money' },
  { key: 'totalRunCost', header: 'Total $/run incl. grading', kind: 'money' },
  { key: 'totalRunCostSd', header: 'Total $/run SD', kind: 'money' },
  { key: 'totalRunCostUnavailable', header: 'Total $/run unavailable because', kind: 'text' },
  { key: 'pricingResolved', header: 'Pricing resolved', kind: 'boolean' },
  { key: 'scheduledChange', header: 'Scheduled price change', kind: 'text' },
  { key: 'differsOn', header: 'Differs on', kind: 'text' },
  { key: 'speedDegradedBy', header: 'Speed degraded by', kind: 'text' },
  { key: 'costDegradedBy', header: 'Cost degraded by', kind: 'text' },
  { key: 'unstableItems', header: 'Unstable items', kind: 'integer' },
  { key: 'explanation', header: 'Explanation', kind: 'text' }
];

// ------------------------------------------------------------------------------------------------
// The display columns
//
// What the admin shows, hides and orders: the nine columns of the on-screen table, some of which
// combine several parts, and the twenty-two remaining parts as columns of their own. Reading formats
// keep a combined column combined; data formats write its parts as typed columns.
// ------------------------------------------------------------------------------------------------

/** How the Interactive table renders a display column's cells. */
export type TableColumnRenderer =
  'model' | 'runs' | 'state' | 'intelligence' | 'speedIndex' | 'timings' | 'cost' | 'totalCost' | 'notes' | 'text';

export interface TableDisplayColumn {
  /** Equal to the part key for a single-value column. */
  readonly key: string;
  /** The on-screen header, which reading formats write too. */
  readonly header: string;
  readonly renderer: TableColumnRenderer;
  /** Keys into {@link COMPARISON_TABLE_COLUMNS}, primary first. */
  readonly parts: readonly string[];
  /** The part the header sorts by; null is not sortable. */
  readonly sortPart: string | null;
}

/** Which display columns are shown, and the order of all of them. */
export interface TableColumnConfig {
  /** Every display key, in the admin's order. */
  readonly order: readonly string[];
  /** The checked ones; always includes {@link TABLE_MODEL_COLUMN_KEY}. */
  readonly shown: readonly string[];
}

/** Every output needs a row header, so the model column cannot be hidden. */
export const TABLE_MODEL_COLUMN_KEY = 'model';

const COMBINED_TABLE_COLUMNS: readonly TableDisplayColumn[] = [
  { key: 'model', header: 'Model', renderer: 'model', parts: ['label', 'thinkingLevel', 'provider', 'source'], sortPart: 'label' },
  { key: 'runs', header: 'R', renderer: 'runs', parts: ['runCount'], sortPart: 'runCount' },
  { key: 'stateCol', header: 'State', renderer: 'state', parts: ['state', 'explanation'], sortPart: 'state' },
  {
    key: 'intelligence',
    header: 'Intelligence Index',
    renderer: 'intelligence',
    parts: ['intelligenceIndex', 'intervalHalfWidth', 'intervalBasis'],
    sortPart: 'intelligenceIndex'
  },
  { key: 'speedIndexCol', header: 'Speed Index', renderer: 'speedIndex', parts: ['speedIndex', 'speedIndexSaturated'], sortPart: 'speedIndex' },
  {
    key: 'timings',
    header: 'Timings',
    renderer: 'timings',
    parts: ['modelTimeMeanMs', 'totalModelTimeMs', 'ttftP50Ms', 'ttftP90Ms'],
    sortPart: 'modelTimeMeanMs'
  },
  {
    key: 'cost',
    header: 'Candidate $ / question',
    renderer: 'cost',
    parts: ['costPerQuestion', 'pricingResolved', 'scheduledChange'],
    sortPart: 'costPerQuestion'
  },
  {
    key: 'totalCost',
    header: 'Total $ / run, with grading',
    renderer: 'totalCost',
    parts: ['totalRunCost', 'totalRunCostSd', 'totalRunCostUnavailable'],
    sortPart: 'totalRunCost'
  },
  {
    key: 'notes',
    header: 'Notes',
    renderer: 'notes',
    parts: ['differsOn', 'speedDegradedBy', 'costDegradedBy', 'unstableItems'],
    sortPart: null
  }
];

/** Parts that are the primary of a combined column and are never offered on their own. */
const COMBINED_PRIMARY_PARTS: ReadonlySet<string> = new Set(['label', 'runCount', 'state', 'intelligenceIndex', 'speedIndex', 'costPerQuestion', 'totalRunCost']);

/**
 * The 31 display columns: the nine of the on-screen table in its order, then every other part as a
 * column of its own, in {@link COMPARISON_TABLE_COLUMNS} order.
 */
export const TABLE_DISPLAY_COLUMNS: readonly TableDisplayColumn[] = [
  ...COMBINED_TABLE_COLUMNS,
  ...COMPARISON_TABLE_COLUMNS
    .filter(column => !COMBINED_PRIMARY_PARTS.has(column.key))
    .map((column): TableDisplayColumn => ({
      key: column.key,
      header: column.header,
      renderer: 'text',
      parts: [column.key],
      sortPart: column.key
    }))
];

const DISPLAY_COLUMNS_BY_KEY: ReadonlyMap<string, TableDisplayColumn> =
  new Map(TABLE_DISPLAY_COLUMNS.map(column => [column.key, column]));

const PART_COLUMNS_BY_KEY: ReadonlyMap<string, ComparisonTableColumn> =
  new Map(COMPARISON_TABLE_COLUMNS.map(column => [column.key, column]));

/** Today's on-screen table: its nine columns shown, in its order, then the rest hidden. */
export const DEFAULT_TABLE_COLUMNS: TableColumnConfig = {
  order: TABLE_DISPLAY_COLUMNS.map(column => column.key),
  shown: COMBINED_TABLE_COLUMNS.map(column => column.key)
};

export function tableDisplayColumn(key: string): TableDisplayColumn | undefined {
  return DISPLAY_COLUMNS_BY_KEY.get(key);
}

/** The shown display columns, in the configuration's order. */
export function shownTableColumns(config: TableColumnConfig): TableDisplayColumn[] {
  const shown = new Set(config.shown);
  return config.order
    .filter(key => shown.has(key))
    .map(key => DISPLAY_COLUMNS_BY_KEY.get(key))
    .filter((column): column is TableDisplayColumn => column !== undefined);
}

/**
 * A usable configuration from anything, a parsed `localStorage` value included: unknown and repeated
 * keys dropped, missing ones appended hidden in catalogue order, and the model column always shown.
 */
export function normalizeTableColumnConfig(value: unknown): TableColumnConfig {
  const record = typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
  const known = (list: unknown): string[] => Array.isArray(list)
    ? [...new Set(list.filter((key): key is string => typeof key === 'string' && DISPLAY_COLUMNS_BY_KEY.has(key)))]
    : [];
  const stored = known(record['order']);
  const order = [...stored, ...TABLE_DISPLAY_COLUMNS.map(column => column.key).filter(key => !stored.includes(key))];
  const shownSet = new Set(Array.isArray(record['shown']) ? known(record['shown']) : DEFAULT_TABLE_COLUMNS.shown);
  shownSet.add(TABLE_MODEL_COLUMN_KEY);
  return { order, shown: order.filter(key => shownSet.has(key)) };
}

/** The saved-layout version this build writes. */
export const TABLE_COLUMN_CONFIG_VERSION = 2;

/**
 * A saved layout brought up to the current version, then normalized. Version 2 introduced the
 * total-cost column: a version-1 layout gets it directly after the candidate cost column, shown
 * when that column is shown.
 */
export function migrateTableColumnConfig(value: unknown): TableColumnConfig {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return normalizeTableColumnConfig(value);
  }
  const record = value as Record<string, unknown>;
  const version = record['version'];
  const order = record['order'];
  if ((version === undefined || (typeof version === 'number' && version < 2))
      && Array.isArray(order) && !order.includes('totalCost')) {
    const costAt = order.indexOf('cost');
    const migratedOrder = costAt < 0
      ? [...order, 'totalCost']
      : [...order.slice(0, costAt + 1), 'totalCost', ...order.slice(costAt + 1)];
    const shown = record['shown'];
    const migratedShown = Array.isArray(shown) && shown.includes('cost') ? [...shown, 'totalCost'] : shown;
    return normalizeTableColumnConfig({ ...record, order: migratedOrder, shown: migratedShown });
  }
  return normalizeTableColumnConfig(record);
}

/** Whether two configurations show the same columns in the same order. */
export function sameTableColumnConfig(a: TableColumnConfig, b: TableColumnConfig): boolean {
  const shownA = shownTableColumns(a).map(column => column.key);
  const shownB = shownTableColumns(b).map(column => column.key);
  return a.order.length === b.order.length
    && a.order.every((key, index) => key === b.order[index])
    && shownA.length === shownB.length
    && shownA.every((key, index) => key === shownB[index]);
}

/**
 * The text a reading format writes for one display column: the parts the no-duplication rule leaves
 * in, worded as the screen shows them. A part shown as a column of its own is left out, so no value
 * is printed twice.
 */
export function readingCellText(
  column: TableDisplayColumn,
  cells: Readonly<Record<string, ComparisonTableCell>>,
  shownKeys: ReadonlySet<string>
): string {
  const kept = (part: string): ComparisonTableCell | null => {
    const cell = cells[part];
    return shownKeys.has(part) || !cell || cell.raw === null ? null : cell;
  };
  const text = (part: string): string => cells[part]?.text ?? ABSENT_TEXT;
  const joined = (pieces: readonly (string | null)[]): string => {
    const present = pieces.filter((piece): piece is string => piece !== null && piece !== '');
    return present.length === 0 ? ABSENT_TEXT : present.join(' · ');
  };

  switch (column.renderer) {
    case 'model': {
      const reasoning = cells[TABLE_REASONING_CELL];
      return joined([
        cells[TABLE_MODEL_NAME_CELL]?.text ?? text('label'),
        kept('thinkingLevel')?.text ?? null,
        reasoning && reasoning.raw !== null ? reasoning.text : null,
        kept('provider')?.text ?? null,
        kept('source')?.text ?? null
      ]);
    }
    case 'runs': {
      const runs = cells['runCount'];
      return runs?.raw === 1 ? `${runs.text} · n = 1` : text('runCount');
    }
    case 'state':
      return joined([text('state'), kept('explanation')?.text ?? null]);
    case 'intelligence': {
      const interval = kept('intervalHalfWidth');
      return joined([
        interval ? `${text('intelligenceIndex')} ± ${interval.text}` : text('intelligenceIndex'),
        kept('intervalBasis')?.text ?? null
      ]);
    }
    case 'speedIndex':
      return joined([text('speedIndex'), kept('speedIndexSaturated')?.raw === true ? 'saturated' : null]);
    case 'timings': {
      const p50 = kept('ttftP50Ms');
      const p90 = kept('ttftP90Ms');
      const ttft = p50 && p90
        ? `TTFT ${p50.text} / ${p90.text}`
        : p50 ? `TTFT P50 ${p50.text}` : p90 ? `TTFT P90 ${p90.text}` : null;
      const mean = kept('modelTimeMeanMs');
      const total = kept('totalModelTimeMs');
      return joined([mean ? `Model ${mean.text}` : null, total ? `Suite ${total.text}` : null, ttft]);
    }
    case 'cost': {
      const scheduled = kept('scheduledChange');
      return joined([
        text('costPerQuestion'),
        kept('pricingResolved')?.raw === false ? 'unpriced' : null,
        scheduled ? `price change ${scheduled.text}` : null
      ]);
    }
    case 'totalCost': {
      const sd = kept('totalRunCostSd');
      const why = kept('totalRunCostUnavailable');
      return joined([
        sd ? `${text('totalRunCost')} ± ${sd.text}` : text('totalRunCost'),
        why ? why.text : null
      ]);
    }
    case 'notes': {
      const differs = kept('differsOn');
      const speed = kept('speedDegradedBy');
      const cost = kept('costDegradedBy');
      const unstable = kept('unstableItems');
      return joined([
        differs ? `Differs on: ${differs.text}` : null,
        speed ? `Speed degraded by: ${speed.text}` : null,
        cost ? `Cost degraded by: ${cost.text}` : null,
        unstable && typeof unstable.raw === 'number' && unstable.raw > 0 ? `${unstable.text} unstable items` : null
      ]);
    }
    case 'text':
      return text(column.parts[0]);
  }
}

// ------------------------------------------------------------------------------------------------
// The on-screen formats
//
// These live here rather than on the component because the export and the table have to agree
// character for character; the component's own template helpers delegate to them.
// ------------------------------------------------------------------------------------------------

/** The absent-measure marker. An unmeasured axis is never printed as zero. */
export const ABSENT_TEXT = '—';

/** One decimal place, which is the precision an Intelligence Index is estimated to. */
export function formatIndexText(value: number | null | undefined): string {
  return value == null || !Number.isFinite(value) ? ABSENT_TEXT : value.toFixed(1);
}

/** Milliseconds below ten seconds, seconds above: `4200 ms`, then `31.0 s`. */
export function formatMsText(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return ABSENT_TEXT;
  }
  return value >= 10000 ? `${(value / 1000).toFixed(1)} s` : `${Math.round(value)} ms`;
}

/** Four decimal places always, so a sub-cent cost and a multi-dollar cost line up. */
export function formatUsdText(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return ABSENT_TEXT;
  }
  return `$${value.toFixed(4)}`;
}

/** `Excluded`, `Degraded` or `Comparable` — the verdict in words, never in a colour. */
export function comparisonStateLabel(entry: BenchmarkModelComparisonEntryDto): string {
  if (entry.excluded) {
    return 'Excluded';
  }
  return entry.state === 'Degraded' ? 'Degraded' : 'Comparable';
}

// ------------------------------------------------------------------------------------------------
// The model
// ------------------------------------------------------------------------------------------------

/**
 * How a model's columns are written. `reading` (the Interactive table, the image, Markdown, HTML)
 * writes one column per shown display column, combined as on screen; `data` (Excel, CSV, TSV, JSON)
 * expands each into its parts as typed columns, in place.
 */
export type TableModelFlavour = 'reading' | 'data';

/**
 * One row per entry, in the order given — which is the caller's filtered, sorted, unpaged view.
 *
 * Excluded entries are included, and carry nulls on every axis: the server returns no measures for
 * one at all, and dropping them here would make an unchartable model invisible in the artefact
 * whose whole job is to say what could not be compared.
 *
 * The columns follow the configuration's order. In the data flavour a part already written by an
 * earlier column is skipped, so no value is written twice. Every encoder iterates `model.columns`,
 * so none needs to know about either rule. `cells`, when given, supplies each entry's precomputed
 * cells by entry key.
 */
export function buildComparisonTableModel(
  entries: readonly BenchmarkModelComparisonEntryDto[],
  provenance: ComparisonTableProvenance,
  config: TableColumnConfig = DEFAULT_TABLE_COLUMNS,
  flavour: TableModelFlavour = 'data',
  cells?: ReadonlyMap<string, Readonly<Record<string, ComparisonTableCell>>>
): ComparisonTableModel {
  const shown = shownTableColumns(config);
  const partCells = (entry: BenchmarkModelComparisonEntryDto): Readonly<Record<string, ComparisonTableCell>> =>
    cells?.get(entry.key) ?? comparisonTableCells(entry);

  if (flavour === 'data') {
    const written = new Set<string>();
    const columns: ComparisonTableColumn[] = [];
    for (const display of shown) {
      for (const part of display.parts) {
        const column = PART_COLUMNS_BY_KEY.get(part);
        if (column && !written.has(part)) {
          written.add(part);
          columns.push(column);
        }
      }
    }
    return { columns, rows: entries.map(entry => ({ cells: partCells(entry) })), provenance };
  }

  const shownKeys = new Set(shown.map(column => column.key));
  const columns: ComparisonTableColumn[] = shown.map(display => ({
    key: display.key,
    header: display.header,
    kind: display.renderer === 'text' ? PART_COLUMNS_BY_KEY.get(display.parts[0])?.kind ?? 'text' : 'text'
  }));
  const rows = entries.map(entry => {
    const parts = partCells(entry);
    const combined: Record<string, ComparisonTableCell> = {};
    for (const display of shown) {
      if (display.renderer !== 'text') {
        combined[display.key] = { raw: parts[display.parts[0]]?.raw ?? null, text: readingCellText(display, parts, shownKeys) };
      }
    }
    return { cells: { ...parts, ...combined } };
  });
  return { columns, rows, provenance };
}

/**
 * Keys of the display columns that hold at least one non-absent part across these rows, in
 * catalogue order.
 *
 * An absent measure is `raw: null`, whether it prints as `—` (a text cell) or was never computed (a
 * numeric one); a `false` boolean and a `0` count are not absent and count as populated.
 */
export function populatedColumnKeys(rows: readonly Readonly<Record<string, ComparisonTableCell>>[]): string[] {
  return TABLE_DISPLAY_COLUMNS
    .filter(column => column.parts.some(part => rows.some(cells => (cells[part]?.raw ?? null) !== null)))
    .map(column => column.key);
}

/** Cells the model renderer reads that no column writes: the display name and the reasoning mode. */
export const TABLE_MODEL_NAME_CELL = 'modelName';
export const TABLE_REASONING_CELL = 'reasoningMode';

/** Every part's cell for one entry, plus the model renderer's two extra cells. */
export function comparisonTableCells(entry: BenchmarkModelComparisonEntryDto): Record<string, ComparisonTableCell> {
  const quality = entry.quality ?? null;
  const speed = entry.speed ?? null;
  const cost = entry.cost ?? null;
  const table = entry.table ?? null;
  const reasoning = entry.reasoningMode ?? null;

  return {
    [TABLE_MODEL_NAME_CELL]: textCell(entry.modelDisplayName || entry.label),
    [TABLE_REASONING_CELL]: textCell(showReasoningBadge(reasoning) ? reasoning : null),
    label: textCell(entry.label),
    source: textCell(`${entry.sourceKind} ${entry.sourceId}`),
    provider: textCell(entry.provider),
    modelId: textCell(entry.modelId),
    thinkingLevel: textCell(entry.thinkingLevel),
    parallelMode: textCell(entry.parallelExecutionMode),
    runCount: countCell(entry.runCount),
    state: textCell(comparisonStateLabel(entry)),
    intelligenceIndex: indexCell(quality?.pointEstimate),
    intervalHalfWidth: indexCell(quality?.intervalHalfWidth),
    intervalBasis: textCell(quality?.intervalBasis),
    modelTimeMeanMs: msCell(speed?.modelTimeMeanMs),
    totalModelTimeMs: msCell(speed?.totalModelTimePerRunMeanMs),
    ttftP50Ms: msCell(speed?.ttftP50Ms),
    ttftP90Ms: msCell(speed?.ttftP90Ms),
    speedIndex: indexCell(table?.meanSpeedIndex),
    speedIndexSaturated: flagCell(table === null ? null : table.speedIndexSaturated),
    costPerQuestion: usdCell(cost?.candidateCostPerQuestionUsd),
    costPerRun: usdCell(cost?.candidateCostPerRunUsd),
    totalRunCost: usdCell(cost?.totalRunCostPerRunUsd),
    totalRunCostSd: usdCell(cost?.totalRunCostSdUsd),
    totalRunCostUnavailable: textCell(cost?.totalRunCostUnavailableReason),
    pricingResolved: flagCell(cost === null ? null : cost.pricingResolved),
    scheduledChange: textCell(cost?.scheduledChangeEffectiveFrom),
    differsOn: listCell(entry.excludingKeys),
    speedDegradedBy: listCell(entry.speedDegradingKeys),
    costDegradedBy: listCell(entry.costDegradingKeys),
    unstableItems: countCell(table?.unstableItemCount),
    explanation: textCell(entry.explanation)
  };
}

function textCell(value: string | null | undefined): ComparisonTableCell {
  const trimmed = (value ?? '').trim();
  return trimmed === ''
    ? { raw: null, text: ABSENT_TEXT }
    : { raw: trimmed, text: trimmed };
}

function listCell(values: readonly string[] | null | undefined): ComparisonTableCell {
  return textCell((values ?? []).join(', '));
}

function countCell(value: number | null | undefined): ComparisonTableCell {
  if (value == null || !Number.isFinite(value)) {
    return { raw: null, text: ABSENT_TEXT };
  }
  return { raw: value, text: String(value) };
}

function indexCell(value: number | null | undefined): ComparisonTableCell {
  const raw = value != null && Number.isFinite(value) ? value : null;
  return { raw, text: formatIndexText(value) };
}

function msCell(value: number | null | undefined): ComparisonTableCell {
  const raw = value != null && Number.isFinite(value) ? Math.round(value) : null;
  return { raw, text: formatMsText(value) };
}

function usdCell(value: number | null | undefined): ComparisonTableCell {
  const raw = value != null && Number.isFinite(value) ? value : null;
  return { raw, text: formatUsdText(value) };
}

function flagCell(value: boolean | null): ComparisonTableCell {
  if (value === null) {
    return { raw: null, text: ABSENT_TEXT };
  }
  return { raw: value, text: value ? 'Yes' : 'No' };
}

// ------------------------------------------------------------------------------------------------
// The text encoders
// ------------------------------------------------------------------------------------------------

/**
 * The characters that make a spreadsheet treat a cell as a formula rather than as text.
 *
 * The leading `-` catches the whole family: `-2+3+cmd|' /C calc'!A0` is the documented injection,
 * and a legitimately negative *number* never reaches the guard because only text columns do.
 */
const FORMULA_LEADERS = ['=', '+', '-', '@', '\t', '\r'];

/** OWASP's prefix. Excel, Calc and Sheets all render the apostrophe as nothing and evaluate nothing. */
function guardFormula(text: string): string {
  return FORMULA_LEADERS.some(leader => text.startsWith(leader)) ? `'${text}` : text;
}

/** The value a delimited format writes for one cell, guarded where the column is free text. */
function delimitedValue(column: ComparisonTableColumn, cell: ComparisonTableCell | undefined): string {
  const text = cell?.text ?? '';
  return column.kind === 'text' ? guardFormula(text) : text;
}

/**
 * RFC 4180: `"` doubled, any field carrying a delimiter, a quote or a line break quoted whole,
 * CRLF between records, and a UTF-8 byte-order mark in front.
 *
 * The BOM is not decoration — Excel on Windows reads a BOM-less UTF-8 CSV as the system code page,
 * which turns every `—` and `±` in this table into mojibake.
 */
export function toCsv(model: ComparisonTableModel): string {
  const escape = (value: string): string =>
    /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;

  const lines = [model.columns.map(column => escape(column.header)).join(',')];
  for (const row of model.rows) {
    lines.push(
      model.columns
        .map(column => escape(delimitedValue(column, row.cells[column.key])))
        .join(',')
    );
  }
  return `\uFEFF${lines.join('\r\n')}\r\n`;
}

/**
 * Tab-separated, with tabs inside a cell replaced by a space.
 *
 * There is no quoting convention every consumer of TSV agrees on, so the delimiter is removed from
 * the data instead: a quoted TSV field is read literally, quotes and all, by half of them.
 */
export function toTsv(model: ComparisonTableModel): string {
  const flatten = (value: string): string => value.replace(/[\t\r\n]+/g, ' ');

  const lines = [model.columns.map(column => flatten(column.header)).join('\t')];
  for (const row of model.rows) {
    lines.push(
      model.columns
        .map(column => flatten(delimitedValue(column, row.cells[column.key])))
        .join('\t')
    );
  }
  return `\uFEFF${lines.join('\r\n')}\r\n`;
}

/** A GFM table under a caption line, with the notices as a list beneath it. */
export function toMarkdown(model: ComparisonTableModel): string {
  const escape = (value: string): string => value.replace(/\|/g, '\\|').replace(/[\r\n]+/g, ' ');

  const lines: string[] = [
    `**Comparison table** — ${provenanceLine(model.provenance)}`,
    '',
    `| ${model.columns.map(column => escape(column.header)).join(' | ')} |`,
    `| ${model.columns.map(() => '---').join(' | ')} |`
  ];
  for (const row of model.rows) {
    lines.push(
      `| ${model.columns.map(column => escape(row.cells[column.key]?.text ?? '')).join(' | ')} |`
    );
  }
  if (model.provenance.notices.length > 0) {
    lines.push('', 'Notices:', '');
    for (const notice of model.provenance.notices) {
      lines.push(`- ${escape(notice)}`);
    }
  }
  return `${lines.join('\n')}\n`;
}

/** The machine shape: provenance, the column declarations, and one object of raw values per row. */
export function toJson(model: ComparisonTableModel): string {
  const rows = model.rows.map(row => {
    const record: Record<string, string | number | boolean | null> = {};
    for (const column of model.columns) {
      record[column.key] = row.cells[column.key]?.raw ?? null;
    }
    return record;
  });
  return `${JSON.stringify({ provenance: model.provenance, columns: model.columns, rows }, null, 2)}\n`;
}

/** Every HTML-significant character escaped, so a cell can never emit markup. */
function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/** A standalone document: one table with a caption, a minimal embedded sheet, and the notices. */
export function toHtml(model: ComparisonTableModel): string {
  const escape = escapeHtml;

  const head = model.columns
    .map(column => `<th scope="col">${escape(column.header)}</th>`)
    .join('');
  const body = model.rows
    .map(row => {
      const cells = model.columns
        .map(column => `<td>${escape(row.cells[column.key]?.text ?? '')}</td>`)
        .join('');
      return `<tr>${cells}</tr>`;
    })
    .join('\n');
  const notices = model.provenance.notices.length === 0
    ? ''
    : `<ul class="notices">\n${model.provenance.notices
      .map(notice => `<li>${escape(notice)}</li>`)
      .join('\n')}\n</ul>\n`;

  return [
    '<!DOCTYPE html>',
    '<html lang="en">',
    '<head>',
    '<meta charset="utf-8">',
    '<title>Comparison table</title>',
    '<style>',
    'body { font-family: system-ui, sans-serif; margin: 24px; color: #1a1a1a; }',
    'table { border-collapse: collapse; font-size: 13px; }',
    'caption { caption-side: top; text-align: left; padding-bottom: 8px; color: #555; }',
    'th, td { border: 1px solid #ccc; padding: 4px 8px; text-align: left; vertical-align: top; }',
    'thead th { background: #f2f2f2; }',
    '.notices { margin-top: 16px; font-size: 13px; color: #555; }',
    '</style>',
    '</head>',
    '<body>',
    '<table>',
    `<caption>Comparison table — ${escape(provenanceLine(model.provenance))}</caption>`,
    `<thead><tr>${head}</tr></thead>`,
    '<tbody>',
    body,
    '</tbody>',
    '</table>',
    notices.trimEnd(),
    '</body>',
    '</html>',
    ''
  ].join('\n');
}

/**
 * A bare `<table>` for the clipboard: a header row of `<th>` and one `<tr>` per row, no document,
 * caption or style around it, which is what a spreadsheet pastes as cells.
 *
 * Numeric columns carry their raw value, so the pasted cells are numbers a reader can sum; text
 * columns carry the guarded text the TSV writes, and an absent measure is an empty cell.
 */
function toHtmlTableFragment(model: ComparisonTableModel): string {
  const value = (column: ComparisonTableColumn, cell: ComparisonTableCell | undefined): string => {
    const raw = cell?.raw ?? null;
    if (column.kind === 'text' || column.kind === 'boolean') {
      return raw === null ? '' : delimitedValue(column, cell);
    }
    return typeof raw === 'number' ? String(raw) : '';
  };
  const head = model.columns.map(column => `<th>${escapeHtml(column.header)}</th>`).join('');
  const body = model.rows
    .map(row => `<tr>${model.columns.map(column => `<td>${escapeHtml(value(column, row.cells[column.key]))}</td>`).join('')}</tr>`)
    .join('');
  return `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
}

/** The UTF-8 byte-order mark the CSV and TSV files carry, which pasted into a cell is a stray character. */
function withoutBom(text: string): string {
  return text.replace(/^﻿/, '');
}

/**
 * What *Copy table* writes for one format: `text` always, and `html` where a formatted paste exists.
 *
 * Excel is the TSV as text and a bare `<table>` fragment as HTML, which Excel, Sheets and Calc paste
 * as cells; HTML is the TSV as text and the standalone document as HTML, which PowerPoint and Word
 * paste as a real table. Markdown, CSV, TSV and JSON are that format as text, without the BOM the
 * files carry. The image is not handled here: the caller composes and copies a PNG instead.
 */
export function tableClipboardPayload(
  model: ComparisonTableModel,
  format: Exclude<TableFileFormat, 'image'>
): { text: string; html?: string } {
  switch (format) {
    case 'xlsx':
      return { text: withoutBom(toTsv(model)), html: toHtmlTableFragment(model) };
    case 'html':
      return { text: withoutBom(toTsv(model)), html: toHtml(model) };
    case 'csv':
      return { text: withoutBom(toCsv(model)) };
    case 'tsv':
      return { text: withoutBom(toTsv(model)) };
    case 'md':
      return { text: toMarkdown(model) };
    case 'json':
      return { text: toJson(model) };
  }
}

/** Suite, basis, condition and time on one line, as every caption and subtitle prints it. */
function provenanceLine(provenance: ComparisonTableProvenance): string {
  const parts = [provenance.suite, provenance.pricingBasis, provenance.plottedOfTotal];
  if (provenance.conditionSignature !== '') {
    parts.push(`condition ${provenance.conditionSignature}`);
  }
  parts.push(`computed at ${provenance.computedAt}`);
  return parts.join(' · ');
}

// ------------------------------------------------------------------------------------------------
// The spreadsheet
// ------------------------------------------------------------------------------------------------

/** The cell shape `write-excel-file` reads, narrowed to what this module writes. */
interface XlsxCell {
  value?: string | number | boolean | null;
  type?: StringConstructor | NumberConstructor | BooleanConstructor;
  format?: string;
  fontWeight?: 'bold';
  wrap?: boolean;
}

interface XlsxSheet {
  sheet: string;
  columns?: { width: number }[];
  /** Rows frozen at the top of the pane. One, so the header survives a scroll. */
  stickyRowsCount?: number;
  data: (XlsxCell | null)[][];
}

/** The one function this module calls, and the object shape version 4 of the library returns. */
export interface XlsxWriterModule {
  default(sheets: readonly XlsxSheet[]): { toBlob(): Promise<Blob> };
}

/**
 * The spreadsheet writer, behind a holder rather than a bare `import()` call.
 *
 * Dynamic, so the admin bundle pays for `write-excel-file` and its zip encoder on the first
 * spreadsheet export rather than on load; behind a holder so a spec can stand a fake in its place,
 * which a bare dynamic import offers no seam for.
 *
 * The entry point is `write-excel-file/browser`: version 4 removed the package root export, and
 * the browser build is the one that produces a `Blob` rather than touching the file system.
 */
export const xlsxWriterModule: { load(): Promise<XlsxWriterModule> } = {
  load: async (): Promise<XlsxWriterModule> =>
    (await import('write-excel-file/browser')) as unknown as XlsxWriterModule
};

/** The widest a derived column may be, in characters. Past this a cell wraps instead. */
const XLSX_MAX_COLUMN_WIDTH = 60;

/** The number format each column kind is written under, so a reader sorts and sums on real values. */
function xlsxFormatOf(kind: ComparisonTableColumnKind): string | undefined {
  switch (kind) {
    case 'number':
      return '0.0';
    case 'integer':
    case 'ms':
      return '#,##0';
    case 'money':
      return '$0.0000';
    default:
      return undefined;
  }
}

function xlsxCellOf(column: ComparisonTableColumn, cell: ComparisonTableCell | undefined): XlsxCell {
  const raw = cell?.raw ?? null;
  if (column.kind === 'boolean') {
    return { value: typeof raw === 'boolean' ? raw : null, type: Boolean };
  }
  if (column.kind === 'text') {
    return { value: typeof raw === 'string' ? raw : null, type: String, wrap: true };
  }
  return { value: typeof raw === 'number' ? raw : null, type: Number, format: xlsxFormatOf(column.kind) };
}

/** A column as wide as its widest cell, header included, and never wider than the wrapping cap. */
function xlsxColumnWidth(model: ComparisonTableModel, column: ComparisonTableColumn): number {
  let longest = column.header.length;
  for (const row of model.rows) {
    longest = Math.max(longest, (row.cells[column.key]?.text ?? '').length);
  }
  return Math.min(XLSX_MAX_COLUMN_WIDTH, Math.max(8, longest + 2));
}

/**
 * Two sheets: *Comparison* with a bold, frozen header row over typed columns, and *Provenance*
 * with the facts that make the first sheet checkable.
 *
 * The second sheet is not a nicety. A spreadsheet is the format most likely to be mailed on with
 * its costs intact and its pricing basis forgotten, and costs taken on two different price cards
 * compare price cards rather than models.
 */
export async function toXlsx(model: ComparisonTableModel): Promise<Blob> {
  const writer = await xlsxWriterModule.load();

  const header: XlsxCell[] = model.columns.map(column => ({
    value: column.header,
    type: String,
    fontWeight: 'bold'
  }));
  const data: XlsxCell[][] = [
    header,
    ...model.rows.map(row => model.columns.map(column => xlsxCellOf(column, row.cells[column.key])))
  ];

  const provenance = model.provenance;
  const facts: [string, string][] = [
    ['Suite', provenance.suite],
    ['Pricing basis', provenance.pricingBasis],
    ['Reference condition', provenance.conditionSignature || ABSENT_TEXT],
    ['Charted', provenance.plottedOfTotal],
    ['Computed at', provenance.computedAt]
  ];
  const provenanceData: XlsxCell[][] = [
    [{ value: 'Fact', type: String, fontWeight: 'bold' }, { value: 'Value', type: String, fontWeight: 'bold' }],
    ...facts.map(([name, value]): XlsxCell[] => [
      { value: name, type: String },
      { value, type: String, wrap: true }
    ]),
    [{ value: 'Notices', type: String, fontWeight: 'bold' }, { value: null, type: String }],
    ...provenance.notices.map((notice): XlsxCell[] => [
      { value: null, type: String },
      { value: notice, type: String, wrap: true }
    ])
  ];

  const sheets: XlsxSheet[] = [
    {
      sheet: 'Comparison',
      stickyRowsCount: 1,
      columns: model.columns.map(column => ({ width: xlsxColumnWidth(model, column) })),
      data
    },
    {
      sheet: 'Provenance',
      columns: [{ width: 22 }, { width: XLSX_MAX_COLUMN_WIDTH }],
      data: provenanceData
    }
  ];

  // Re-wrapped rather than returned as the library handed it over: the media type is what a
  // consumer dispatches on, and it is the one thing a stand-in writer cannot be trusted to set.
  const written = await writer.default(sheets).toBlob();
  return new Blob([written], { type: XLSX_MEDIA_TYPE });
}

// ------------------------------------------------------------------------------------------------
// The image
//
// One layout serves two sizes. *Fit the table* lays the table out at its natural size — each
// column as wide as its widest text up to the widest cap that keeps the image inside the dimension
// limit, each row as tall as its wrapped text — and writes that box at a density. A *box* is an
// exact requested size: one layout px is `textScale` requested px, the columns take the widest cap
// that fits the box's width and share whatever width is left over, and a table that does not fit is
// refused with the width or height that would fit it. The measurement, the refusals and the
// drawing all read the same layout.
// ------------------------------------------------------------------------------------------------

/** Layout constants, in CSS pixels before the density transform is applied. */
const TABLE_PADDING = 20;
const TABLE_TITLE_SIZE = 18;
const TABLE_SUBTITLE_SIZE = 12;
const TABLE_TEXT_SIZE = 11;
const TABLE_CELL_PADDING_X = 8;
const TABLE_CELL_PADDING_Y = 5;
const TABLE_LINE_GAP = 6;
const TABLE_RULE_GAP = 10;

/** The image's own title line. */
const TABLE_IMAGE_TITLE = 'Comparison table';

/** The narrowest an image may be laid out, so a two-column model still reads as a figure. */
const TABLE_MIN_CONTENT_WIDTH = 360;

/** The narrowest a column may be squeezed to. `R` and `±` want to be this narrow. */
const TABLE_MIN_COLUMN_WIDTH = 44;

/**
 * The per-column width caps tried in order, widest first.
 *
 * Twenty-four columns of unbounded prose — `Explanation` alone runs to a sentence — would compose
 * several times wider than any image format is worth writing, so the first cap under which the
 * whole table fits is the one used, and the text wraps inside it.
 */
const TABLE_COLUMN_CAPS: readonly number[] = [260, 220, 180, 150, 120, 96, 76, 60];

/**
 * The dark theme's alternating row band, faint enough to guide the eye across 24 columns without
 * striping loudly. The composer draws `theme.chrome.tableBand`, which resolves to this by default.
 */
export const TABLE_BAND_COLOR = 'rgba(255, 255, 255, 0.035)';

/** The density of the default *Fit the table* image. Reading it at 1x would export the text blurred. */
export const TABLE_IMAGE_SCALE = 2;

/**
 * Absorbs floating-point noise when a layout length is turned into requested px, so a size that
 * {@link measureTableImage} reports is always one the box layout accepts.
 */
const REQUESTED_PX_EPSILON = 1e-6;

/** One column, laid out: how wide it is drawn and how its header wraps inside it. */
interface TableImageColumn {
  readonly column: ComparisonTableColumn;
  readonly width: number;
  readonly headerLines: string[];
}

/** Everything the composer draws, wrapped to one set of column widths and one content width. */
interface TableImageContent {
  readonly columns: readonly TableImageColumn[];
  /** The width the title, the bands and the rules span: the table's, or the box's less its padding. */
  readonly contentWidth: number;
  readonly titleLines: string[];
  readonly subtitleLines: string[];
  readonly noticeLines: readonly string[][];
  readonly headerHeight: number;
  /** Per row, per column, the wrapped lines. */
  readonly bodyRows: readonly string[][][];
  readonly bodyHeights: readonly number[];
  /** The whole image's height in layout px, padding included. */
  readonly height: number;
}

/** A resolved table image: its layout and what is drawn in it, in the theme and row style given. */
interface TableImagePlan {
  readonly layout: TableImageLayout;
  readonly content: TableImageContent;
  readonly theme: ResolvedFigureTheme;
  readonly style: TableImageStyle;
}

/**
 * Resolves the table image's layout without drawing it, or refuses it.
 *
 * *Fit the table* (`size.mode === 'fit'`, the default at {@link TABLE_IMAGE_SCALE}) is the table's
 * natural size at the density, lowered where either side would pass
 * {@link FIGURE_EXPORT_MAX_DIMENSION}: a table long enough to overrun it is written at a lower
 * density rather than truncated. A box is laid out at `widthPx / textScale` × `heightPx /
 * textScale` layout px and written at exactly `widthPx × density` × `heightPx × density`; it is
 * refused when the bitmap would pass the cap, when even the narrowest column cap is wider than the
 * box, or when the wrapped rows are taller than it, and each refusal names what would fit.
 *
 * The preview, the buttons' disabled state and the download all consult this, and
 * {@link composeTableImage} draws from the same layout, so none of them can disagree.
 */
export function resolveTableImageLayout(
  model: ComparisonTableModel,
  options: TableImageOptions = {}
): { layout: TableImageLayout | null; refusal: string | null } {
  const { plan, refusal } = planTableImage(model, options);
  return { layout: plan?.layout ?? null, refusal };
}

/**
 * The *Fit the table* size in requested px at `textScale`: the table's natural width and height
 * times the text size, each rounded up.
 *
 * Typed back in as a box at the same text size, these numbers fit the table with no spare space,
 * and one pixel less in height is refused. One pixel less in width is refused too while no column
 * cap is binding; where one is, the box accepts the narrower width at the next cap down and wraps
 * the text further, and refuses only once its rows no longer fit the height. The row style draws
 * inside the rows and never changes the size.
 */
export function measureTableImage(
  model: ComparisonTableModel,
  options: { theme?: ResolvedFigureTheme; tableStyle?: TableImageStyle; textScale: number }
): { widthPx: number; heightPx: number } {
  const theme = options.theme ?? resolveFigureTheme();
  const type = tableTypography(theme);
  const measurer = tableMeasurer(type.stack);
  const textScale = isPositiveNumber(options.textScale) ? options.textScale : 1;

  const natural = naturalColumnWidths(model, measurer, type);
  const widths = fitColumnWidths(natural, textScale);
  const widthPx = requestedPx(boxedWidth(widths), textScale);
  // The height is the box layout's at exactly that width, so typing both numbers back fits.
  const boxed = boxColumnWidths(natural, widthPx, textScale);
  const content = boxed.kind === 'fits'
    ? layoutTableContent(model, boxed.widths, boxed.contentWidth, measurer, type)
    : layoutTableContent(model, widths, Math.max(columnsSpan(widths), TABLE_MIN_CONTENT_WIDTH), measurer, type);
  return { widthPx, heightPx: requestedPx(content.height, textScale) };
}

/**
 * Draws the whole table as an image, in the theme's colours, font stack and weights.
 *
 * The same palette and the same font as the figure composer, so a table pasted beside a figure in
 * one document reads as part of it. The title and the header row take the heading weight, the
 * cells the label weight; the ground is painted and the border drawn exactly as a figure's. Row
 * bands and row rules follow `tableStyle`. Every option absent draws the dark, banded *Fit the
 * table* image at {@link TABLE_IMAGE_SCALE}.
 *
 * The bitmap is the layout's own pixel size. `previewRaster`, when given, is the device px per
 * layout px instead, for a preview drawn at display resolution; the composition is unchanged. A
 * refused size returns a 1 × 1 canvas, so callers consult {@link resolveTableImageLayout} first.
 */
export function composeTableImage(
  model: ComparisonTableModel,
  options: TableImageOptions & { previewRaster?: number } = {}
): HTMLCanvasElement {
  const { plan } = planTableImage(model, options);
  if (!plan) {
    const empty = document.createElement('canvas');
    empty.width = 1;
    empty.height = 1;
    return empty;
  }
  return drawTableImage(plan, options.previewRaster);
}

/** Encodes a composed table canvas, through the figure encoder and its WebP fallback reporting. */
export function encodeTableImage(
  canvas: HTMLCanvasElement,
  format: 'png' | 'webp',
  quality?: WebpQuality
): Promise<FigureExportResult> {
  return encodeFigureImage(canvas, format, quality);
}

/**
 * `model-comparison_table_<yyyyMMdd_HHmmss>.<ext>`.
 *
 * Local time, and sortable, so a set of exports taken in one sitting lands in one contiguous block
 * in a file listing, next to the figures written from the same comparison.
 */
export function tableExportFilename(format: TableExportFormat, now: Date = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  const stamp =
    `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_` +
    `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  return `model-comparison_table_${stamp}.${format}`;
}

/** The media type each text format is written under, so a saved file opens in the right application. */
const TABLE_MEDIA_TYPES: Record<string, string> = {
  csv: 'text/csv;charset=utf-8',
  tsv: 'text/tab-separated-values;charset=utf-8',
  md: 'text/markdown;charset=utf-8',
  json: 'application/json',
  html: 'text/html;charset=utf-8'
};

/**
 * One model, one format, one blob.
 *
 * The single place the eight formats are dispatched, so the component holds no second copy of the
 * mapping and a format added here reaches the view by adding one `<option>`. The image options
 * (`theme`, `tableStyle`, `size`) are read only for PNG and WebP; absent, they draw the dark,
 * banded *Fit the table* image at {@link TABLE_IMAGE_SCALE}. A refused image size rejects with the
 * refusal text rather than writing a 1 × 1 image.
 */
export async function encodeComparisonTable(
  model: ComparisonTableModel,
  format: TableExportFormat,
  options?: { webpQuality?: WebpQuality } & TableImageOptions
): Promise<TableExportResult> {
  if (format === 'png' || format === 'webp') {
    const { plan, refusal } = planTableImage(model, {
      theme: options?.theme,
      tableStyle: options?.tableStyle,
      size: options?.size
    });
    if (!plan) {
      throw new Error(refusal ?? 'The table image could not be laid out.');
    }
    const encoded = await encodeTableImage(drawTableImage(plan), format, options?.webpQuality);
    return { blob: encoded.blob, format: encoded.format, fellBackToPng: encoded.fellBackToPng };
  }
  if (format === 'xlsx') {
    return { blob: await toXlsx(model), format, fellBackToPng: false };
  }

  const text = format === 'csv'
    ? toCsv(model)
    : format === 'tsv'
      ? toTsv(model)
      : format === 'md'
        ? toMarkdown(model)
        : format === 'json'
          ? toJson(model)
          : toHtml(model);
  return {
    blob: new Blob([text], { type: TABLE_MEDIA_TYPES[format] ?? 'text/plain;charset=utf-8' }),
    format,
    fellBackToPng: false
  };
}

// ------------------------------------------------------------------------------------------------
// Image internals
// ------------------------------------------------------------------------------------------------

/** The font stack and the two weights the table image is measured and drawn in. */
interface TableTypography {
  readonly stack: string;
  /** The title and the header row. */
  readonly heading: string;
  /** The body cells. */
  readonly label: string;
}

function tableTypography(theme: ResolvedFigureTheme): TableTypography {
  return {
    stack: theme.fonts.chromeStack,
    heading: String(theme.fonts.headingWeight),
    label: String(theme.fonts.labelWeight)
  };
}

/** Text measuring and wrapping in one font stack, on one scratch context. */
interface TableMeasurer {
  widthOf(text: string, size: number, weight: string): number;
  wrap(text: string, width: number, size: number, weight: string): string[];
}

function tableMeasurer(stack: string): TableMeasurer {
  const measure = document.createElement('canvas').getContext('2d');
  return {
    widthOf: (text, size, weight) => {
      if (!measure) {
        return text.length * size * 0.6;
      }
      measure.font = `${weight} ${size}px ${stack}`;
      return measure.measureText(text).width;
    },
    wrap: (text, width, size, weight) =>
      measure ? wrapText(measure, text, width, size, weight, stack) : (text === '' ? [] : [text])
  };
}

function isPositiveNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

/** The requested px `layoutPx` layout px take at `textScale`, rounded up. */
function requestedPx(layoutPx: number, textScale: number): number {
  return Math.ceil(layoutPx * textScale - REQUESTED_PX_EPSILON);
}

/** Each column's widest text: its header in the heading weight, its cells in the label weight. */
function naturalColumnWidths(
  model: ComparisonTableModel,
  measurer: TableMeasurer,
  type: TableTypography
): number[] {
  return model.columns.map(column => {
    let widest = measurer.widthOf(column.header, TABLE_TEXT_SIZE, type.heading);
    for (const row of model.rows) {
      widest = Math.max(widest, measurer.widthOf(row.cells[column.key]?.text ?? '', TABLE_TEXT_SIZE, type.label));
    }
    return widest;
  });
}

function cappedWidths(natural: readonly number[], cap: number): number[] {
  return natural.map(value => Math.max(TABLE_MIN_COLUMN_WIDTH, Math.min(value, cap)));
}

/** The columns' drawn width, their cell padding included. */
function columnsSpan(widths: readonly number[]): number {
  return widths.reduce((total, width) => total + width + TABLE_CELL_PADDING_X * 2, 0);
}

/** The whole image's width for these columns: at least the minimum content width, plus the padding. */
function boxedWidth(widths: readonly number[]): number {
  return Math.max(columnsSpan(widths), TABLE_MIN_CONTENT_WIDTH) + TABLE_PADDING * 2;
}

/**
 * The widest cap under which the composed width, times `limitScale`, fits the dimension limit, and
 * the column widths it produces. Falls through to the narrowest cap rather than failing: a squeezed
 * table is still a readable one, and there is no honest way to refuse a set the reader has already
 * filtered down to.
 */
function fitColumnWidths(natural: readonly number[], limitScale: number): number[] {
  let widths: number[] = [];
  for (const cap of TABLE_COLUMN_CAPS) {
    widths = cappedWidths(natural, cap);
    const composed = columnsSpan(widths) + TABLE_PADDING * 2;
    if (composed * limitScale <= FIGURE_EXPORT_MAX_DIMENSION) {
      break;
    }
  }
  return widths;
}

/**
 * The column widths for a box `widthPx` requested px wide at `textScale`: the widest cap whose
 * image fits the width, with the spare width shared among the columns in proportion to their capped
 * widths, so the table spans the box. `narrow` carries the width the narrowest cap needs.
 */
function boxColumnWidths(
  natural: readonly number[],
  widthPx: number,
  textScale: number
): { kind: 'fits'; widths: number[]; contentWidth: number } | { kind: 'narrow'; minimumWidthPx: number } {
  const contentWidth = widthPx / textScale - TABLE_PADDING * 2;
  let minimumWidthPx = 0;
  for (const cap of TABLE_COLUMN_CAPS) {
    const widths = cappedWidths(natural, cap);
    minimumWidthPx = requestedPx(boxedWidth(widths), textScale);
    if (minimumWidthPx <= widthPx) {
      const spare = Math.max(0, contentWidth - columnsSpan(widths));
      const total = widths.reduce((sum, width) => sum + width, 0);
      const shared = total > 0 ? widths.map(width => width + spare * width / total) : widths;
      return { kind: 'fits', widths: shared, contentWidth };
    }
  }
  return { kind: 'narrow', minimumWidthPx };
}

/** Wraps every text of the table to these column widths and this content width, and sums the height. */
function layoutTableContent(
  model: ComparisonTableModel,
  widths: readonly number[],
  contentWidth: number,
  measurer: TableMeasurer,
  type: TableTypography
): TableImageContent {
  const columns: TableImageColumn[] = model.columns.map((column, index) => ({
    column,
    width: widths[index],
    headerLines: measurer.wrap(column.header, widths[index], TABLE_TEXT_SIZE, type.heading)
  }));
  const titleLines = measurer.wrap(TABLE_IMAGE_TITLE, contentWidth, TABLE_TITLE_SIZE, type.heading);
  const subtitleLines = measurer.wrap(provenanceLine(model.provenance), contentWidth, TABLE_SUBTITLE_SIZE, '400');
  const noticeLines = model.provenance.notices.map(
    notice => measurer.wrap(notice, contentWidth, TABLE_SUBTITLE_SIZE, '400')
  );

  const headerHeight = rowHeight(Math.max(1, ...columns.map(entry => entry.headerLines.length)));
  const bodyRows = model.rows.map(row =>
    columns.map(entry => measurer.wrap(row.cells[entry.column.key]?.text ?? '', entry.width, TABLE_TEXT_SIZE, type.label))
  );
  const bodyHeights = bodyRows.map(cells => rowHeight(Math.max(1, ...cells.map(lines => lines.length))));

  let height = TABLE_PADDING * 2;
  height += blockHeight(titleLines, TABLE_TITLE_SIZE);
  height += blockHeight(subtitleLines, TABLE_SUBTITLE_SIZE);
  height += TABLE_LINE_GAP + headerHeight + TABLE_RULE_GAP;
  height += bodyHeights.reduce((total, rowHeightPx) => total + rowHeightPx, 0);
  height += TABLE_RULE_GAP;
  for (const lines of noticeLines) {
    height += TABLE_LINE_GAP + blockHeight(lines, TABLE_SUBTITLE_SIZE);
  }

  return { columns, contentWidth, titleLines, subtitleLines, noticeLines, headerHeight, bodyRows, bodyHeights, height };
}

/** The one layout {@link resolveTableImageLayout}, {@link composeTableImage} and the encoder share. */
function planTableImage(
  model: ComparisonTableModel,
  options: TableImageOptions
): { plan: TableImagePlan | null; refusal: string | null } {
  const theme = options.theme ?? resolveFigureTheme();
  const style = options.tableStyle ?? DEFAULT_TABLE_IMAGE_STYLE;
  const size: TableImageSize = options.size ?? { mode: 'fit', density: TABLE_IMAGE_SCALE };
  const type = tableTypography(theme);
  const measurer = tableMeasurer(type.stack);
  const counts = { columnCount: model.columns.length, rowCount: model.rows.length };

  if (size.mode === 'fit') {
    if (!isPositiveNumber(size.density)) {
      return { plan: null, refusal: 'The table image needs a positive pixel density.' };
    }
    const widths = fitColumnWidths(naturalColumnWidths(model, measurer, type), size.density);
    const contentWidth = Math.max(columnsSpan(widths), TABLE_MIN_CONTENT_WIDTH);
    const content = layoutTableContent(model, widths, contentWidth, measurer, type);
    const imageWidth = contentWidth + TABLE_PADDING * 2;
    const imageHeight = content.height;

    // The last guarantee on both sides: a table long enough to overrun the height limit is written
    // at a lower density rather than truncated, because a truncated table is a wrong one.
    const density = Math.max(
      0.1,
      Math.min(
        size.density,
        FIGURE_EXPORT_MAX_DIMENSION / Math.max(1, imageWidth),
        FIGURE_EXPORT_MAX_DIMENSION / Math.max(1, imageHeight)
      )
    );
    const layout: TableImageLayout = {
      layoutWidth: imageWidth,
      layoutHeight: imageHeight,
      scale: density,
      pixelWidth: Math.max(1, Math.floor(imageWidth * density)),
      pixelHeight: Math.max(1, Math.floor(imageHeight * density)),
      ...counts
    };
    return { plan: { layout, content, theme, style }, refusal: null };
  }

  const { widthPx, heightPx, density, textScale } = size;
  if (!isPositiveNumber(density) || !isPositiveNumber(textScale)) {
    return { plan: null, refusal: 'The table image needs a positive pixel density and text size.' };
  }
  if (!isPositiveNumber(widthPx) || !isPositiveNumber(heightPx)) {
    return { plan: null, refusal: 'The table image needs a positive width and height.' };
  }
  const oversized = bitmapRefusal(widthPx, heightPx, density);
  if (oversized) {
    return { plan: null, refusal: oversized };
  }

  const boxed = boxColumnWidths(naturalColumnWidths(model, measurer, type), widthPx, textScale);
  if (boxed.kind === 'narrow') {
    const minimum = boxed.minimumWidthPx;
    return {
      plan: null,
      refusal:
        `The ${counts.columnCount} selected columns need at least ${minimum} px of width at this text size. ` +
        `Choose fewer columns in the Table tab, a width of ${minimum} px or more, or a smaller text size.`
    };
  }

  const content = layoutTableContent(model, boxed.widths, boxed.contentWidth, measurer, type);
  const minimumHeight = requestedPx(content.height, textScale);
  if (minimumHeight > heightPx) {
    return {
      plan: null,
      refusal:
        `The table's ${counts.rowCount} rows need at least ${minimumHeight} px of height at this width and text size. ` +
        `Choose a height of ${minimumHeight} px or more, a smaller text size, or Fit the table.`
    };
  }

  const layout: TableImageLayout = {
    layoutWidth: widthPx / textScale,
    layoutHeight: heightPx / textScale,
    scale: textScale * density,
    pixelWidth: Math.round(widthPx * density),
    pixelHeight: Math.round(heightPx * density),
    ...counts
  };
  return { plan: { layout, content, theme, style }, refusal: null };
}

/** Draws a resolved plan, top-aligned in its box, at its own scale or at `previewRaster`. */
function drawTableImage(plan: TableImagePlan, previewRaster?: number): HTMLCanvasElement {
  const { layout, content, theme, style } = plan;
  const previewing = isPositiveNumber(previewRaster);
  const raster = isPositiveNumber(previewRaster) ? previewRaster : layout.scale;

  const target = document.createElement('canvas');
  target.width = previewing ? Math.max(1, Math.round(layout.layoutWidth * raster)) : layout.pixelWidth;
  target.height = previewing ? Math.max(1, Math.round(layout.layoutHeight * raster)) : layout.pixelHeight;

  const context = target.getContext('2d');
  if (!context) {
    return target;
  }
  context.scale(raster, raster);

  const type = tableTypography(theme);
  const colors = theme.chrome;
  paintFigureBackground(context, layout.layoutWidth, layout.layoutHeight, theme);
  context.textBaseline = 'top';

  let y = TABLE_PADDING;
  y = drawLines(context, content.titleLines, TABLE_PADDING, y, TABLE_TITLE_SIZE, type.heading, colors.title, type.stack);
  y = drawLines(context, content.subtitleLines, TABLE_PADDING, y, TABLE_SUBTITLE_SIZE, '400', colors.muted, type.stack);
  y += TABLE_LINE_GAP;

  drawRowCells(
    context,
    content.columns,
    content.columns.map(entry => entry.headerLines),
    y,
    TABLE_TEXT_SIZE,
    type.heading,
    colors.title,
    type.stack
  );
  y += content.headerHeight;
  y = drawRule(context, y, layout.layoutWidth, colors.rule);

  const lastRow = content.bodyRows.length - 1;
  content.bodyRows.forEach((cells, index) => {
    const height = content.bodyHeights[index];
    if (style.rowBands && index % 2 === 1) {
      context.fillStyle = colors.tableBand;
      context.fillRect(TABLE_PADDING, y, content.contentWidth, height);
    }
    drawRowCells(context, content.columns, cells, y, TABLE_TEXT_SIZE, type.label, colors.body, type.stack);
    // The last row is closed by the table's own rule just below it.
    if (style.rowRules && index < lastRow) {
      drawRowRule(context, y + height, content.contentWidth, colors.rule);
    }
    y += height;
  });

  y = drawRule(context, y, layout.layoutWidth, colors.rule);
  for (const lines of content.noticeLines) {
    y += TABLE_LINE_GAP;
    y = drawLines(context, lines, TABLE_PADDING, y, TABLE_SUBTITLE_SIZE, '400', colors.muted, type.stack);
  }

  drawFigureBorder(context, layout.layoutWidth, layout.layoutHeight, theme.border);
  return target;
}

function lineHeightOf(size: number): number {
  return Math.round(size * 1.4);
}

function blockHeight(lines: readonly string[], size: number): number {
  return lines.length === 0 ? 0 : lines.length * lineHeightOf(size);
}

function rowHeight(lineCount: number): number {
  return lineCount * lineHeightOf(TABLE_TEXT_SIZE) + TABLE_CELL_PADDING_Y * 2;
}

function drawLines(
  context: CanvasRenderingContext2D,
  lines: readonly string[],
  x: number,
  y: number,
  size: number,
  weight: string,
  color: string,
  stack: string
): number {
  if (lines.length === 0) {
    return y;
  }
  context.font = `${weight} ${size}px ${stack}`;
  context.fillStyle = color;
  let cursor = y;
  for (const line of lines) {
    context.fillText(line, x, cursor);
    cursor += lineHeightOf(size);
  }
  return cursor;
}

/** One row of cells across the layout, each wrapped block drawn inside its own column box. */
function drawRowCells(
  context: CanvasRenderingContext2D,
  layout: readonly TableImageColumn[],
  cells: readonly string[][],
  y: number,
  size: number,
  weight: string,
  color: string,
  stack: string
): void {
  let x = TABLE_PADDING;
  layout.forEach((entry, index) => {
    drawLines(
      context, cells[index] ?? [], x + TABLE_CELL_PADDING_X, y + TABLE_CELL_PADDING_Y, size, weight, color, stack);
    x += entry.width + TABLE_CELL_PADDING_X * 2;
  });
}

function drawRule(context: CanvasRenderingContext2D, y: number, imageWidth: number, color: string): number {
  const at = Math.round(y + TABLE_RULE_GAP / 2) + 0.5;
  context.strokeStyle = color;
  context.lineWidth = 1;
  context.beginPath();
  context.moveTo(TABLE_PADDING, at);
  context.lineTo(imageWidth - TABLE_PADDING, at);
  context.stroke();
  return y + TABLE_RULE_GAP;
}

/** A hairline along the bottom edge of one body row, inside the row's own height. */
function drawRowRule(context: CanvasRenderingContext2D, bottom: number, contentWidth: number, color: string): void {
  const at = Math.round(bottom) - 0.5;
  context.strokeStyle = color;
  context.lineWidth = 1;
  context.beginPath();
  context.moveTo(TABLE_PADDING, at);
  context.lineTo(TABLE_PADDING + contentWidth, at);
  context.stroke();
}
