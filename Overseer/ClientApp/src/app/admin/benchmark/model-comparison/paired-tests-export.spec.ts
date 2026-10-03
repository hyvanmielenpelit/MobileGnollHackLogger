import {
  BenchmarkPairedComparisonDto,
  BenchmarkPairedMeasureDto,
  BenchmarkPairedTestDto
} from '../../../services/admin-benchmark.service';
import {
  PAIRED_CSV_HEADERS,
  pairedExportFormat,
  pairedExportRows,
  pairedFamilyLine,
  pairedTestsCsv,
  pairedTestsFilename,
  pairedTestsMarkdown,
  pairedTestsMediaType
} from './paired-tests-export';

function pair(baselineKey: string, treatmentKey: string, overrides: Partial<BenchmarkPairedTestDto> = {}): BenchmarkPairedTestDto {
  return {
    baselineKey,
    treatmentKey,
    pairedItems: 18,
    unpairedItems: 0,
    revisionMismatched: 0,
    effect: 3.2,
    effectLower: -0.8,
    effectUpper: 7.1,
    effectKind: 'Difference',
    dz: 0.45,
    pValue: 0.012,
    adjustedPValue: 0.024,
    method: 'Wilcoxon signed-rank',
    direction: 'Higher',
    established: true,
    verdict: 'Higher on the same questions',
    notTestedReason: null,
    note: null,
    suites: null,
    ...overrides
  };
}

function measure(
  name: string,
  category: BenchmarkPairedMeasureDto['category'],
  pairs: BenchmarkPairedTestDto[],
  overrides: Partial<BenchmarkPairedMeasureDto> = {}
): BenchmarkPairedMeasureDto {
  return {
    measure: name,
    label: name === 'Intelligence' ? 'Intelligence Index' : name,
    category,
    primary: category === 'Intelligence',
    familySize: pairs.length,
    adjustment: 'Holm',
    adjustmentNote: `Holm-adjusted across ${pairs.length} tests`,
    notTestedReason: null,
    caption: null,
    pairs,
    ...overrides
  };
}

function dto(overrides: Partial<BenchmarkPairedComparisonDto> = {}): BenchmarkPairedComparisonDto {
  const ratio = (b: string, t: string): BenchmarkPairedTestDto => pair(b, t, {
    effectKind: 'Ratio', effect: 0.82, effectLower: 0.71, effectUpper: 0.95, dz: null, verdict: 'Faster on the same questions'
  });
  return {
    computedAtUtc: '2026-10-03T12:00:00Z',
    subjectKind: 'Runs',
    pricingBasis: 'Current',
    mode: 'Reference',
    referenceKey: 'run:1',
    entryKeys: ['run:1', 'run:2', 'run:3'],
    allPairsLimit: 12,
    singleRunCaveat: 'At least one entry has a single run.',
    measuresNote: 'Each measure is its own family of tests.',
    measures: [
      measure('Intelligence', 'Intelligence', [pair('run:1', 'run:2'), pair('run:1', 'run:3', { effect: -1.5, established: false, verdict: 'No difference established' })]),
      measure('Speed', 'Speed', [ratio('run:1', 'run:2'), ratio('run:1', 'run:3')]),
      measure('Cost', 'Cost', [], { familySize: 0, notTestedReason: 'The cost axis is degraded: the price lists differ.' })
    ],
    ...overrides
  };
}

const LABELS: Record<string, string> = { 'run:1': 'Model A', 'run:2': 'Model B | fast', 'run:3': 'Model C, large' };
const labelOf = (key: string): string => LABELS[key] ?? key;

describe('paired-tests-export', () => {
  it('writes Markdown for the reading formats and CSV for the data formats', () => {
    expect(pairedExportFormat('md')).toBe('md');
    expect(pairedExportFormat('html')).toBe('md');
    expect(pairedExportFormat('image')).toBe('md');
    expect(pairedExportFormat('csv')).toBe('csv');
    expect(pairedExportFormat('tsv')).toBe('csv');
    expect(pairedExportFormat('xlsx')).toBe('csv');
    expect(pairedExportFormat('json')).toBe('csv');
    expect(pairedTestsMediaType('md')).toContain('text/markdown');
    expect(pairedTestsMediaType('csv')).toContain('text/csv');
  });

  it('lists one row per measure and pair, in the server\'s order, with a row for a measure not tested', () => {
    const rows = pairedExportRows(dto(), labelOf);
    expect(rows.map(row => `${row.measure}:${row.treatmentKey}`)).toEqual([
      'Intelligence:run:2', 'Intelligence:run:3', 'Speed:run:2', 'Speed:run:3', 'Cost:'
    ]);
    expect(rows[0]).toMatchObject({ treatment: 'Model B | fast', baseline: 'Model A', effectText: '+3.2 (−0.8 to 7.1)' });
    expect(rows[2].effectText).toBe('0.82× (0.71–0.95)');
    expect(rows[4]).toMatchObject({ verdict: 'Not tested', notTested: 'The cost axis is degraded: the price lists differ.' });
  });

  it('names the family: one test, tests against a reference, or every pair', () => {
    expect(pairedFamilyLine(dto(), labelOf)).toBe('2 tests against Model A · Holm-adjusted');
    expect(pairedFamilyLine(dto({ mode: 'AllPairs', referenceKey: null }), labelOf)).toBe('3 tests, every pair of 3 models · Holm-adjusted');
    expect(pairedFamilyLine(dto({ entryKeys: ['run:1', 'run:2'] }), labelOf))
      .toBe('1 test: Model B | fast against Model A · Single comparison — no adjustment needed');
  });

  it('writes Markdown: the family, the notes, then one table per measure with escaped pipes', () => {
    const markdown = pairedTestsMarkdown(dto(), labelOf);
    const lines = markdown.split('\n');
    expect(lines[0]).toBe('**Paired tests** — computed 2026-10-03T12:00:00Z, prices: Current');
    expect(markdown).toContain('2 tests against Model A · Holm-adjusted');
    expect(markdown).toContain('At least one entry has a single run.');
    expect(markdown).toContain('### Intelligence Index (primary test)');
    expect(markdown).toContain('| Model | Against | Paired questions | Difference (95 % interval) | dz | p | Adjusted p | Verdict |');
    expect(markdown).toContain('| Model | Against | Paired questions | Ratio (95 % interval) | dz | p | Adjusted p | Verdict |');
    expect(markdown).toContain('| Model B \\| fast | Model A | 18 | +3.2 (−0.8 to 7.1) | +0.45 | 0.01 | 0.02 | Higher on the same questions |');
    expect(markdown).toContain('Not tested: The cost axis is degraded: the price lists differ.');
    expect(markdown).not.toContain('\r');
    expect(markdown.endsWith('\n')).toBe(true);
    // Every data row has the header's eight cells, so no pipe broke a row.
    const rows = lines.filter(line => line.startsWith('| ') && !line.startsWith('| Model |') && !line.startsWith('| ---'));
    expect(rows.length).toBe(4);
    expect(rows.every(row => row.split(/(?<!\\)\|/).length === 10)).toBe(true);
  });

  it('writes CSV with raw numbers, quoted where needed, a BOM and CRLF line endings', () => {
    const csv = pairedTestsCsv(dto(), labelOf);
    expect(csv.startsWith('﻿')).toBe(true);
    const lines = csv.slice(1).split('\r\n');
    expect(lines[0]).toBe(PAIRED_CSV_HEADERS.join(','));
    expect(lines.at(-1)).toBe('');
    expect(lines.length - 2).toBe(5);
    expect(lines[1].startsWith('Intelligence,yes,Model B | fast,run:2,Model A,run:1,18,Difference,3.2,-0.8,7.1,0.45,0.012,0.024,Holm,2,Higher on the same questions,yes,,')).toBe(true);
    expect(lines[2]).toContain('"Model C, large"');
    expect(lines[5]).toContain('Cost,no,,,,,,Ratio,,,,,,,Holm,0,Not tested,no,The cost axis is degraded: the price lists differ.');
  });

  it('names the file by format and local time', () => {
    const at = new Date(2026, 9, 3, 7, 5, 9);
    expect(pairedTestsFilename('md', at)).toBe('paired-tests_20261003-070509.md');
    expect(pairedTestsFilename('csv', at)).toBe('paired-tests_20261003-070509.csv');
  });
});
