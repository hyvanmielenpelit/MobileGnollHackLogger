import { BenchmarkPairedComparisonDto, BenchmarkPairedMeasureDto } from '../../../services/admin-benchmark.service';
import {
  ABSENT,
  formatDz,
  formatEffect,
  formatP,
  measureEffectKind,
  measureTitle,
  notTestedReasonOf,
  verdictText
} from '../shared/paired-test/paired-test-format';
import type { TableFileFormat } from './table-export';

// The Paired tests view's Copy as Markdown and Download: one row per measure and pair, in the view's
// order, so the copy, the Markdown file and the CSV file carry the same rows.

/** The two formats the Paired tests view writes. */
export type PairedExportFormat = 'md' | 'csv';

/** One measure and pair, as every export writes it. */
export interface PairedExportRow {
  readonly measure: string;
  readonly measureTitle: string;
  readonly category: BenchmarkPairedMeasureDto['category'];
  readonly treatmentKey: string;
  readonly treatment: string;
  readonly baselineKey: string;
  readonly baseline: string;
  readonly pairedItems: number | null;
  readonly effectKind: 'Difference' | 'Ratio';
  /** The effect with its interval, as the view prints it. */
  readonly effectText: string;
  readonly effect: number | null;
  readonly effectLower: number | null;
  readonly effectUpper: number | null;
  readonly dz: number | null;
  readonly pValue: number | null;
  readonly adjustedPValue: number | null;
  readonly adjustment: BenchmarkPairedMeasureDto['adjustment'];
  readonly adjustmentNote: string;
  readonly verdict: string;
  readonly established: boolean;
  /** Why the pair, or its whole measure, was not tested; empty when it was. */
  readonly notTested: string;
  readonly method: string;
  readonly note: string;
}

/** The Download tab's table format decides the file: the reading formats write Markdown, the data formats CSV. */
export function pairedExportFormat(tableFormat: TableFileFormat): PairedExportFormat {
  return tableFormat === 'md' || tableFormat === 'html' || tableFormat === 'image' ? 'md' : 'csv';
}

/** Every measure's pairs, in the server's measure order (Intelligence first) and pair order. */
export function pairedExportRows(
  dto: BenchmarkPairedComparisonDto,
  labelOf: (key: string) => string
): PairedExportRow[] {
  const rows: PairedExportRow[] = [];
  for (const measure of dto.measures) {
    const kind = measureEffectKind(measure);
    if (measure.pairs.length === 0) {
      rows.push({
        measure: measure.measure, measureTitle: measureTitle(measure), category: measure.category,
        treatmentKey: '', treatment: '', baselineKey: '', baseline: '', pairedItems: null,
        effectKind: kind, effectText: ABSENT, effect: null, effectLower: null, effectUpper: null,
        dz: null, pValue: null, adjustedPValue: null, adjustment: measure.adjustment,
        adjustmentNote: measure.adjustmentNote, verdict: 'Not tested', established: false,
        notTested: measure.notTestedReason ?? '', method: '', note: ''
      });
      continue;
    }
    for (const pair of measure.pairs) {
      rows.push({
        measure: measure.measure,
        measureTitle: measureTitle(measure),
        category: measure.category,
        treatmentKey: pair.treatmentKey,
        treatment: labelOf(pair.treatmentKey),
        baselineKey: pair.baselineKey,
        baseline: labelOf(pair.baselineKey),
        pairedItems: pair.pairedItems,
        effectKind: pair.effectKind,
        effectText: formatEffect(pair),
        effect: pair.effect ?? null,
        effectLower: pair.effectLower ?? null,
        effectUpper: pair.effectUpper ?? null,
        dz: pair.dz ?? null,
        pValue: pair.pValue ?? null,
        adjustedPValue: pair.adjustedPValue ?? null,
        adjustment: measure.adjustment,
        adjustmentNote: measure.adjustmentNote,
        verdict: verdictText(pair),
        established: pair.established,
        notTested: notTestedReasonOf(measure, pair) ?? '',
        method: pair.method,
        note: pair.note ?? ''
      });
    }
  }
  return rows;
}

/** A Markdown cell: a pipe escaped so the column count survives, line breaks flattened. */
function markdownCell(value: string): string {
  return value.replace(/\|/g, '\\|').replace(/[\r\n]+/g, ' ');
}

/** The family line: how many tests, against what, and the adjustment. */
export function pairedFamilyLine(dto: BenchmarkPairedComparisonDto, labelOf: (key: string) => string): string {
  const count = dto.entryKeys.length;
  const reference = dto.referenceKey ? labelOf(dto.referenceKey) : '';
  if (count === 2) {
    const other = dto.entryKeys.find(key => key !== dto.referenceKey) ?? dto.entryKeys[1];
    return `1 test: ${labelOf(other)} against ${reference} · Single comparison — no adjustment needed`;
  }
  if (dto.mode === 'AllPairs') {
    return `${count * (count - 1) / 2} tests, every pair of ${count} models · Holm-adjusted`;
  }
  return `${count - 1} tests against ${reference} · Holm-adjusted`;
}

/**
 * A heading line, the family line, the notes, then one GFM table per measure under its title and
 * adjustment note. LF line endings, as the comparison table's Markdown.
 */
export function pairedTestsMarkdown(dto: BenchmarkPairedComparisonDto, labelOf: (key: string) => string): string {
  const rows = pairedExportRows(dto, labelOf);
  const lines: string[] = [
    `**Paired tests** — computed ${dto.computedAtUtc}, prices: ${dto.pricingBasis}`,
    '',
    pairedFamilyLine(dto, labelOf),
    '',
    dto.measuresNote
  ];
  if (dto.singleRunCaveat) {
    lines.push('', dto.singleRunCaveat);
  }
  for (const measure of dto.measures) {
    const measureRows = rows.filter(row => row.measure === measure.measure);
    const effectHeader = measureEffectKind(measure) === 'Ratio' ? 'Ratio (95 % interval)' : 'Difference (95 % interval)';
    lines.push('', `### ${markdownCell(measureTitle(measure))}`, '', markdownCell(measure.adjustmentNote));
    if (measure.caption) {
      lines.push('', markdownCell(measure.caption));
    }
    if (measure.notTestedReason) {
      lines.push('', `Not tested: ${markdownCell(measure.notTestedReason)}`);
    }
    if (measure.pairs.length === 0) {
      continue;
    }
    lines.push(
      '',
      `| Model | Against | Paired questions | ${effectHeader} | dz | p | Adjusted p | Verdict |`,
      '| --- | --- | ---: | --- | ---: | ---: | ---: | --- |'
    );
    for (const row of measureRows) {
      const verdict = row.notTested ? `Not tested: ${row.notTested}` : row.verdict;
      lines.push(`| ${[
        row.treatment, row.baseline, row.pairedItems === null ? ABSENT : String(row.pairedItems), row.effectText,
        formatDz(row.dz), formatP(row.pValue), formatP(row.adjustedPValue), verdict
      ].map(markdownCell).join(' | ')} |`);
    }
  }
  return `${lines.join('\n')}\n`;
}

/** The CSV columns, in order. */
export const PAIRED_CSV_HEADERS: readonly string[] = [
  'Measure', 'Primary', 'Model', 'Model key', 'Against', 'Against key', 'Paired questions', 'Effect kind',
  'Effect', 'Lower 95 %', 'Upper 95 %', 'dz', 'p', 'Adjusted p', 'Adjustment', 'Family size', 'Verdict',
  'Established', 'Not tested', 'Method', 'Note'
];

/** A raw number for a data file: full precision, empty when absent. */
function csvNumber(value: number | null): string {
  return value === null || !Number.isFinite(value) ? '' : String(value);
}

/**
 * One row per measure and pair with raw numbers, quoted where needed, with a BOM and CRLF line
 * endings so Excel on Windows reads it as UTF-8, as the comparison table's CSV.
 */
export function pairedTestsCsv(dto: BenchmarkPairedComparisonDto, labelOf: (key: string) => string): string {
  const escape = (value: string): string => /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
  const familySize = new Map(dto.measures.map(measure => [measure.measure, measure.familySize]));
  const primary = new Map(dto.measures.map(measure => [measure.measure, measure.primary]));
  const lines = [PAIRED_CSV_HEADERS.map(escape).join(',')];
  for (const row of pairedExportRows(dto, labelOf)) {
    lines.push([
      row.measure, primary.get(row.measure) ? 'yes' : 'no', row.treatment, row.treatmentKey, row.baseline,
      row.baselineKey, row.pairedItems === null ? '' : String(row.pairedItems), row.effectKind,
      csvNumber(row.effect), csvNumber(row.effectLower), csvNumber(row.effectUpper), csvNumber(row.dz),
      csvNumber(row.pValue), csvNumber(row.adjustedPValue), row.adjustment, String(familySize.get(row.measure) ?? ''),
      row.verdict, row.established ? 'yes' : 'no', row.notTested, row.method, row.note
    ].map(escape).join(','));
  }
  return `\uFEFF${lines.join('\r\n')}\r\n`;
}

/** `paired-tests_<yyyyMMdd-HHmmss>.<md|csv>`, in local time. */
export function pairedTestsFilename(format: PairedExportFormat, now: Date = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-`
    + `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  return `paired-tests_${stamp}.${format}`;
}

/** The file's media type. */
export function pairedTestsMediaType(format: PairedExportFormat): string {
  return format === 'md' ? 'text/markdown;charset=utf-8' : 'text/csv;charset=utf-8';
}
