/**
 * Turns the comparison table into a file, in eight formats.
 *
 * Pure functions over one model: no Angular, no component state, no `TableState`, so the whole
 * module unit-tests without a fixture and the component keeps only the wiring.
 *
 * Four things here are load-bearing rather than incidental:
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
 */

import type { BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import {
  FIGURE_BACKGROUND,
  FIGURE_BODY_COLOR,
  FIGURE_EXPORT_MAX_DIMENSION,
  FIGURE_EXPORT_SCALE,
  FIGURE_FONT_STACK,
  FIGURE_MUTED_COLOR,
  FIGURE_RULE_COLOR,
  FIGURE_TITLE_COLOR,
  encodeFigureImage,
  wrapText
} from './figure-export';
import type { FigureExportResult } from './figure-export';

/** The eight offered formats. Each id is also the file extension it is written under. */
export type TableExportFormat = 'xlsx' | 'csv' | 'tsv' | 'md' | 'json' | 'html' | 'png' | 'webp';

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
 * Twenty-four of them — every field the on-screen table renders plus the configuration keys it
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
  { key: 'ttftP50Ms', header: 'TTFT P50 ms', kind: 'ms' },
  { key: 'ttftP90Ms', header: 'TTFT P90 ms', kind: 'ms' },
  { key: 'speedIndex', header: 'Speed Index', kind: 'number' },
  { key: 'speedIndexSaturated', header: 'Saturated', kind: 'boolean' },
  { key: 'costPerQuestion', header: 'Candidate $/question', kind: 'money' },
  { key: 'costPerRun', header: 'Candidate $/run', kind: 'money' },
  { key: 'pricingResolved', header: 'Pricing resolved', kind: 'boolean' },
  { key: 'scheduledChange', header: 'Scheduled price change', kind: 'text' },
  { key: 'differsOn', header: 'Differs on', kind: 'text' },
  { key: 'speedDegradedBy', header: 'Speed degraded by', kind: 'text' },
  { key: 'costDegradedBy', header: 'Cost degraded by', kind: 'text' },
  { key: 'unstableItems', header: 'Unstable items', kind: 'integer' },
  { key: 'explanation', header: 'Explanation', kind: 'text' }
];

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

/** Four decimal places under a cent, two above: a candidate cost per question is usually the first. */
export function formatUsdText(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) {
    return ABSENT_TEXT;
  }
  if (value === 0) {
    return '$0';
  }
  return value < 0.01 ? `$${value.toFixed(4)}` : `$${value.toFixed(2)}`;
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
 * One row per entry, in the order given — which is the caller's filtered, sorted, unpaged view.
 *
 * Excluded entries are included, and carry nulls on every axis: the server returns no measures for
 * one at all, and dropping them here would make an unchartable model invisible in the artefact
 * whose whole job is to say what could not be compared.
 */
export function buildComparisonTableModel(
  entries: readonly BenchmarkModelComparisonEntryDto[],
  provenance: ComparisonTableProvenance
): ComparisonTableModel {
  return {
    columns: COMPARISON_TABLE_COLUMNS,
    rows: entries.map(entry => ({ cells: cellsOf(entry) })),
    provenance
  };
}

function cellsOf(entry: BenchmarkModelComparisonEntryDto): Record<string, ComparisonTableCell> {
  const quality = entry.quality ?? null;
  const speed = entry.speed ?? null;
  const cost = entry.cost ?? null;
  const table = entry.table ?? null;

  return {
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
    ttftP50Ms: msCell(speed?.ttftP50Ms),
    ttftP90Ms: msCell(speed?.ttftP90Ms),
    speedIndex: indexCell(table?.meanSpeedIndex),
    speedIndexSaturated: flagCell(table === null ? null : table.speedIndexSaturated),
    costPerQuestion: usdCell(cost?.candidateCostPerQuestionUsd),
    costPerRun: usdCell(cost?.candidateCostPerRunUsd),
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

/** A standalone document: one table with a caption, a minimal embedded sheet, and the notices. */
export function toHtml(model: ComparisonTableModel): string {
  const escape = (value: string): string =>
    value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');

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

/** The narrowest an image may be laid out, so a two-column model still reads as a figure. */
const TABLE_MIN_CONTENT_WIDTH = 360;

/** The narrowest a column may be squeezed to. `R` and `±` want to be this narrow. */
const TABLE_MIN_COLUMN_WIDTH = 44;

/**
 * The per-column width caps tried in order, widest first.
 *
 * Twenty-four columns of unbounded prose — `Explanation` alone runs to a sentence — would compose
 * several times wider than any image format is worth writing, so the first cap under which the
 * whole table fits the dimension limit is the one used, and the text wraps inside it.
 */
const TABLE_COLUMN_CAPS: readonly number[] = [260, 220, 180, 150, 120, 96, 76, 60];

/** Alternating row bands, faint enough to guide the eye across 24 columns without striping loudly. */
const TABLE_BAND_COLOR = 'rgba(255, 255, 255, 0.035)';

/** One column, laid out: how wide it is drawn and how each cell's text wraps inside it. */
interface TableImageColumn {
  readonly column: ComparisonTableColumn;
  readonly width: number;
  readonly headerLines: string[];
}

/**
 * Draws the whole table as an image on the figure ground.
 *
 * The same palette and the same font stack as the figure composer, so a table pasted beside
 * a figure in one document reads as part of it; the same opaque background, for the same reason a
 * figure has one.
 *
 * Neither side may exceed {@link FIGURE_EXPORT_MAX_DIMENSION}. Width is brought under it by
 * narrowing the widest text columns until they fit — the table gets taller rather than being
 * cropped — and the density is the final guarantee on both sides.
 */
export function composeTableImage(
  model: ComparisonTableModel,
  options: { scale: number }
): HTMLCanvasElement {
  const measure = document.createElement('canvas').getContext('2d');
  const scale = Number.isFinite(options.scale) && options.scale > 0 ? options.scale : 1;

  const wrap = (text: string, width: number, size: number, weight: string): string[] =>
    measure ? wrapText(measure, text, width, size, weight) : (text === '' ? [] : [text]);
  const widthOf = (text: string, size: number, weight: string): number => {
    if (!measure) {
      return text.length * size * 0.6;
    }
    measure.font = `${weight} ${size}px ${FIGURE_FONT_STACK}`;
    return measure.measureText(text).width;
  };

  const layout = resolveTableColumns(model, scale, widthOf, wrap);
  const tableWidth = layout.reduce(
    (total, entry) => total + entry.width + TABLE_CELL_PADDING_X * 2,
    0
  );
  const contentWidth = Math.max(tableWidth, TABLE_MIN_CONTENT_WIDTH);
  const imageWidth = contentWidth + TABLE_PADDING * 2;

  const titleLines = wrap('Comparison table', contentWidth, TABLE_TITLE_SIZE, '600');
  const subtitleLines = wrap(
    provenanceLine(model.provenance),
    contentWidth,
    TABLE_SUBTITLE_SIZE,
    '400'
  );
  const noticeLines = model.provenance.notices.map(
    notice => wrap(notice, contentWidth, TABLE_SUBTITLE_SIZE, '400')
  );

  const headerHeight = rowHeight(Math.max(1, ...layout.map(entry => entry.headerLines.length)));
  const bodyRows = model.rows.map(row =>
    layout.map(entry => wrap(row.cells[entry.column.key]?.text ?? '', entry.width, TABLE_TEXT_SIZE, '400'))
  );
  const bodyHeights = bodyRows.map(cells => rowHeight(Math.max(1, ...cells.map(lines => lines.length))));

  let imageHeight = TABLE_PADDING * 2;
  imageHeight += blockHeight(titleLines, TABLE_TITLE_SIZE);
  imageHeight += blockHeight(subtitleLines, TABLE_SUBTITLE_SIZE);
  imageHeight += TABLE_LINE_GAP + headerHeight + TABLE_RULE_GAP;
  imageHeight += bodyHeights.reduce((total, height) => total + height, 0);
  imageHeight += TABLE_RULE_GAP;
  for (const lines of noticeLines) {
    imageHeight += TABLE_LINE_GAP + blockHeight(lines, TABLE_SUBTITLE_SIZE);
  }

  // The last guarantee on both sides: a table long enough to overrun the height limit is written
  // at a lower density rather than truncated, because a truncated table is a wrong one.
  const density = Math.max(
    0.1,
    Math.min(
      scale,
      FIGURE_EXPORT_MAX_DIMENSION / Math.max(1, imageWidth),
      FIGURE_EXPORT_MAX_DIMENSION / Math.max(1, imageHeight)
    )
  );

  const target = document.createElement('canvas');
  target.width = Math.max(1, Math.floor(imageWidth * density));
  target.height = Math.max(1, Math.floor(imageHeight * density));

  const context = target.getContext('2d');
  if (!context) {
    return target;
  }
  context.scale(density, density);

  context.fillStyle = FIGURE_BACKGROUND;
  context.fillRect(0, 0, imageWidth, imageHeight);
  context.textBaseline = 'top';

  let y = TABLE_PADDING;
  y = drawLines(context, titleLines, TABLE_PADDING, y, TABLE_TITLE_SIZE, '600', FIGURE_TITLE_COLOR);
  y = drawLines(context, subtitleLines, TABLE_PADDING, y, TABLE_SUBTITLE_SIZE, '400', FIGURE_MUTED_COLOR);
  y += TABLE_LINE_GAP;

  drawRowCells(
    context,
    layout,
    layout.map(entry => entry.headerLines),
    y,
    TABLE_TEXT_SIZE,
    '600',
    FIGURE_TITLE_COLOR
  );
  y += headerHeight;
  y = drawRule(context, y, imageWidth);

  bodyRows.forEach((cells, index) => {
    const height = bodyHeights[index];
    if (index % 2 === 1) {
      context.fillStyle = TABLE_BAND_COLOR;
      context.fillRect(TABLE_PADDING, y, contentWidth, height);
    }
    drawRowCells(context, layout, cells, y, TABLE_TEXT_SIZE, '400', FIGURE_BODY_COLOR);
    y += height;
  });

  y = drawRule(context, y, imageWidth);
  for (const lines of noticeLines) {
    y += TABLE_LINE_GAP;
    y = drawLines(context, lines, TABLE_PADDING, y, TABLE_SUBTITLE_SIZE, '400', FIGURE_MUTED_COLOR);
  }

  return target;
}

/** Encodes a composed table canvas, through the figure encoder and its WebP fallback reporting. */
export function encodeTableImage(
  canvas: HTMLCanvasElement,
  format: 'png' | 'webp'
): Promise<FigureExportResult> {
  return encodeFigureImage(canvas, format);
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
 * mapping and a format added here reaches the view by adding one `<option>`.
 */
export async function encodeComparisonTable(
  model: ComparisonTableModel,
  format: TableExportFormat
): Promise<TableExportResult> {
  if (format === 'png' || format === 'webp') {
    const canvas = composeTableImage(model, { scale: FIGURE_EXPORT_SCALE });
    const encoded = await encodeTableImage(canvas, format);
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

/**
 * The widest cap under which the composed width fits the dimension limit, and the column widths it
 * produces. Falls through to the narrowest cap rather than failing: a squeezed table is still a
 * readable one, and there is no honest way to refuse a set the reader has already filtered down to.
 */
function resolveTableColumns(
  model: ComparisonTableModel,
  scale: number,
  widthOf: (text: string, size: number, weight: string) => number,
  wrap: (text: string, width: number, size: number, weight: string) => string[]
): TableImageColumn[] {
  const natural = model.columns.map(column => {
    let widest = widthOf(column.header, TABLE_TEXT_SIZE, '600');
    for (const row of model.rows) {
      widest = Math.max(widest, widthOf(row.cells[column.key]?.text ?? '', TABLE_TEXT_SIZE, '400'));
    }
    return widest;
  });

  let widths: number[] = [];
  for (const cap of TABLE_COLUMN_CAPS) {
    widths = natural.map(value => Math.max(TABLE_MIN_COLUMN_WIDTH, Math.min(value, cap)));
    const composed = widths.reduce((total, width) => total + width + TABLE_CELL_PADDING_X * 2, 0)
      + TABLE_PADDING * 2;
    if (composed * scale <= FIGURE_EXPORT_MAX_DIMENSION) {
      break;
    }
  }

  return model.columns.map((column, index) => ({
    column,
    width: widths[index],
    headerLines: wrap(column.header, widths[index], TABLE_TEXT_SIZE, '600')
  }));
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
  color: string
): number {
  if (lines.length === 0) {
    return y;
  }
  context.font = `${weight} ${size}px ${FIGURE_FONT_STACK}`;
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
  color: string
): void {
  let x = TABLE_PADDING;
  layout.forEach((entry, index) => {
    drawLines(context, cells[index] ?? [], x + TABLE_CELL_PADDING_X, y + TABLE_CELL_PADDING_Y, size, weight, color);
    x += entry.width + TABLE_CELL_PADDING_X * 2;
  });
}

function drawRule(context: CanvasRenderingContext2D, y: number, imageWidth: number): number {
  const at = Math.round(y + TABLE_RULE_GAP / 2) + 0.5;
  context.strokeStyle = FIGURE_RULE_COLOR;
  context.lineWidth = 1;
  context.beginPath();
  context.moveTo(TABLE_PADDING, at);
  context.lineTo(imageWidth - TABLE_PADDING, at);
  context.stroke();
  return y + TABLE_RULE_GAP;
}
