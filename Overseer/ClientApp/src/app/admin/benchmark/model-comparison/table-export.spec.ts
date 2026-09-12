import { FIGURE_EXPORT_MAX_DIMENSION } from './figure-export';
import type { BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import {
  COMPARISON_TABLE_COLUMNS,
  ComparisonTableModel,
  ComparisonTableProvenance,
  XLSX_MEDIA_TYPE,
  buildComparisonTableModel,
  composeTableImage,
  formatUsdText,
  tableExportFilename,
  toCsv,
  toHtml,
  toJson,
  toMarkdown,
  toTsv,
  toXlsx,
  xlsxWriterModule
} from './table-export';

describe('table-export', () => {
  /** A fully measured comparable entry, so an assertion about an absent value cannot pass by luck. */
  function buildEntry(overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
    return {
      key: 'run:1',
      sourceKind: 'Run',
      sourceId: 43,
      sourceName: null,
      runIds: [43],
      runCount: 3,
      suiteId: 5,
      suiteName: 'GnollHack Player Assistance Benchmark Suite',
      provider: 'Google',
      modelId: 'gemini-2.5-flash',
      modelDisplayName: 'Gemini 2.5 Flash',
      thinkingLevel: 'medium',
      reasoningMode: null,
      reasoningSummary: null,
      serviceTier: null,
      maxOutputTokens: 8192,
      parallelExecutionMode: 'Enabled',
      label: 'Gemini 2.5 Flash',
      firstRunStartedAtUtc: '2026-09-01T10:00:00Z',
      lastRunStartedAtUtc: '2026-09-02T10:00:00Z',
      state: 'Comparable',
      comparable: true,
      excluded: false,
      speedDegraded: false,
      costDegraded: false,
      excludingKeys: [],
      speedDegradingKeys: [],
      costDegradingKeys: [],
      differences: [],
      explanation: 'Comparable with the baseline on every must-match key.',
      quality: {
        pointEstimate: 68,
        itemCount: 18,
        intervalHalfWidth: 6.4,
        intervalLower: 61.6,
        intervalUpper: 74.4,
        intervalTruncated: false,
        itemSamplingHalfWidth: 5.9,
        reproducibilityHalfWidth: 2.5,
        reproducibilityStandardDeviation: 1.4,
        reproducibilityAvailable: true,
        intervalBasis: 'item sampling and reproducibility'
      },
      speed: {
        ttftP50Ms: 4200,
        ttftP90Ms: 12500,
        ttftAnswerCount: 54,
        modelTimeP50Ms: 31000,
        modelTimeP90Ms: 72000,
        pooledAnswerCount: 54,
        degraded: false,
        degradedReason: null,
        caveat: 'Timings were recorded under parallel question execution.'
      },
      cost: {
        candidateCostPerQuestionUsd: 0.0432,
        candidateCostPerRunUsd: 0.7776,
        candidateTotalCostUsd: 2.3328,
        basis: 'Current',
        pricingAsOf: '2026-09-01',
        pricingResolved: true,
        degraded: false,
        degradedReason: null,
        scheduledChangeEffectiveFrom: null,
        scheduledChangeNote: null
      },
      table: {
        meanSpeedIndex: 88,
        speedIndexSaturated: false,
        speedIndexCeilingAnswerCount: 3,
        speedIndexScoredAnswerCount: 54,
        costPerIndexPointUsd: 0.0032,
        meanStoredQualityIndex: 67.4,
        unstableItemCount: 2
      },
      ...overrides
    };
  }

  function provenance(
    overrides: Partial<ComparisonTableProvenance> = {}
  ): ComparisonTableProvenance {
    return {
      suite: 'GnollHack Player Assistance Benchmark Suite',
      pricingBasis: 'Current catalog, as of 2026-09-07',
      conditionSignature: '9c79137965e4',
      computedAt: '7/09/2026, 12:00:00',
      plottedOfTotal: '4 of 5 entries charted',
      notices: ['Cost bars carry no interval at any R.'],
      ...overrides
    };
  }

  function model(
    entries: BenchmarkModelComparisonEntryDto[] = [buildEntry()],
    overrides: Partial<ComparisonTableProvenance> = {}
  ): ComparisonTableModel {
    return buildComparisonTableModel(entries, provenance(overrides));
  }

  /** The data lines of a delimited export, with the byte-order mark and the header row skipped. */
  function dataLines(text: string): string[] {
    return text.replace(/^\uFEFF/, '').trimEnd().split('\r\n').slice(1);
  }

  /**
   * Stands a fake writer in for the dynamically imported one and hands back what it was called
   * with. The holder exists for exactly this: a bare `import()` offers no seam to intercept.
   */
  function captureXlsx(): { sheets: any[] } {
    const captured: { sheets: any[] } = { sheets: [] };
    spyOn(xlsxWriterModule, 'load').and.returnValue(Promise.resolve({
      default: (given: any) => {
        captured.sheets = given;
        return { toBlob: () => Promise.resolve(new Blob(['xlsx-bytes'])) };
      }
    } as any));
    return captured;
  }

  // -------------------------------------------------------------------------------------------
  // The model
  // -------------------------------------------------------------------------------------------

  it('declares twenty-four columns, and a cell for every one of them on every row', () => {
    const built = model([buildEntry(), buildEntry({ key: 'run:2', sourceId: 44 })]);

    expect(built.columns.length).toBe(24);
    expect(built.rows.length).toBe(2);
    for (const row of built.rows) {
      expect(Object.keys(row.cells).length).toBe(24);
    }
  });

  it('carries the machine value beside the on-screen text in every cell', () => {
    const [row] = model().rows;

    expect(row.cells['costPerQuestion'].raw).toBe(0.0432);
    // Two decimals at or above a cent, four below, exactly as the on-screen table prints it.
    expect(row.cells['costPerQuestion'].text).toBe('$0.04');
    expect(formatUsdText(0.0043)).toBe('$0.0043');
    expect(row.cells['ttftP90Ms'].raw).toBe(12500);
    // Ten seconds and over reads in seconds, exactly as the on-screen table prints it.
    expect(row.cells['ttftP90Ms'].text).toBe('12.5 s');
    expect(row.cells['source'].text).toBe('Run 43');
  });

  it('reports an absent measure as null and as the on-screen dash, never as zero', () => {
    const excluded = buildEntry({
      key: 'run:9',
      state: 'Excluded',
      comparable: false,
      excluded: true,
      excludingKeys: ['ScoringMethodVersion'],
      quality: null,
      speed: null,
      cost: null,
      table: null
    });
    const [row] = model([excluded]).rows;

    expect(row.cells['intelligenceIndex'].raw).toBeNull();
    expect(row.cells['intelligenceIndex'].text).toBe('—');
    expect(row.cells['costPerQuestion'].raw).toBeNull();
    expect(row.cells['pricingResolved'].raw).toBeNull();
    expect(row.cells['state'].text).toBe('Excluded');
    expect(row.cells['differsOn'].text).toBe('ScoringMethodVersion');
  });

  // -------------------------------------------------------------------------------------------
  // CSV and TSV
  // -------------------------------------------------------------------------------------------

  it('writes RFC 4180 CSV: a BOM, CRLF records, and quoted fields with doubled quotes', () => {
    const text = toCsv(model([buildEntry({
      label: 'Gemini "Flash", medium',
      explanation: 'Line one\nline two'
    })]));

    expect(text.startsWith('\uFEFF')).toBeTrue();
    expect(text).toContain('\r\n');
    expect(text).toContain('"Gemini ""Flash"", medium"');
    // A line break inside a field is legal, and stays inside the quotes rather than ending a record.
    expect(text).toContain('"Line one\nline two"');
  });

  it('separates records with CRLF and nothing else', () => {
    // A bare LF record separator is what makes Excel on Windows read a one-column table.
    const text = toCsv(model([buildEntry(), buildEntry({ key: 'run:2', sourceId: 44 })]));

    expect(text.replace(/\r\n/g, '')).not.toContain('\n');
    expect(dataLines(text).length).toBe(2);
  });

  it('prefixes a text cell that a spreadsheet would evaluate as a formula', () => {
    const injected = toCsv(model([buildEntry({ label: '=1+cmd|\' /C calc\'!A0' })]));
    const negative = toCsv(model([buildEntry({ label: '-SUM(A1:A2)' })]));
    const at = toCsv(model([buildEntry({ label: '@reference' })]));

    expect(dataLines(injected)[0]).toContain("'=1+cmd");
    expect(dataLines(negative)[0]).toContain("'-SUM(A1:A2)");
    expect(dataLines(at)[0]).toContain("'@reference");
  });

  it('leaves a numeric cell unprefixed, whatever sign it carries', () => {
    // Numbers are never evaluated as formulas, and an apostrophe in front of one would make the
    // column unusable in the spreadsheet it was exported for.
    const text = toCsv(model([buildEntry({
      quality: { ...buildEntry().quality!, pointEstimate: -3.5 }
    })]));

    expect(dataLines(text)[0]).toContain(',-3.5,');
    expect(dataLines(text)[0]).not.toContain("'-3.5");
  });

  it('writes TSV with the delimiter removed from the data rather than quoted around it', () => {
    const text = toTsv(model([buildEntry({ explanation: 'before\tafter' })]));

    expect(text.startsWith('\uFEFF')).toBeTrue();
    const columns = dataLines(text)[0].split('\t');
    expect(columns.length).toBe(24);
    expect(columns[23]).toBe('before after');
  });

  // -------------------------------------------------------------------------------------------
  // Markdown, JSON and HTML
  // -------------------------------------------------------------------------------------------

  it('escapes a pipe in a Markdown cell so the column count survives', () => {
    const text = toMarkdown(model([buildEntry({ label: 'Flash | medium' })]));
    const rows = text.split('\n').filter(line => line.startsWith('|'));

    expect(text).toContain('Flash \\| medium');
    // Header, separator and one body row, each with the same number of cells.
    expect(rows.length).toBe(3);
    expect(rows.map(row => row.split(' | ').length)).toEqual([24, 24, 24]);
  });

  it('carries the provenance as a caption and the notices as a list', () => {
    const text = toMarkdown(model());

    expect(text.split('\n')[0]).toContain('Current catalog, as of 2026-09-07');
    expect(text.split('\n')[0]).toContain('condition 9c79137965e4');
    expect(text).toContain('- Cost bars carry no interval at any R.');
  });

  it('writes JSON of raw values, the column declarations and the provenance', () => {
    const parsed = JSON.parse(toJson(model())) as {
      provenance: ComparisonTableProvenance;
      columns: { key: string }[];
      rows: Record<string, unknown>[];
    };

    expect(parsed.columns.length).toBe(24);
    expect(parsed.columns.map(column => column.key))
      .toEqual(COMPARISON_TABLE_COLUMNS.map(column => column.key));
    // Numbers, not the strings the human formats carry.
    expect(parsed.rows[0]['costPerQuestion']).toBe(0.0432);
    expect(parsed.rows[0]['speedIndexSaturated']).toBeFalse();
    expect(parsed.provenance.pricingBasis).toBe('Current catalog, as of 2026-09-07');
    expect(parsed.provenance.notices.length).toBe(1);
  });

  it('escapes every HTML-significant character rather than emitting markup from a cell', () => {
    const text = toHtml(model([buildEntry({ label: '<script>alert("x")</script>' })]));

    expect(text).toContain('&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;');
    expect(text).not.toContain('<script>');
    expect(text).toContain('<caption>');
    expect(text).toContain('scope="col"');
    expect(text).toContain('<ul class="notices">');
  });

  // -------------------------------------------------------------------------------------------
  // The filename
  // -------------------------------------------------------------------------------------------

  it('names the file after a sortable local timestamp and the format', () => {
    const at = new Date(2026, 8, 12, 18, 30, 12);

    expect(tableExportFilename('xlsx', at)).toBe('model-comparison_table_20260912_183012.xlsx');
    expect(tableExportFilename('md', at)).toBe('model-comparison_table_20260912_183012.md');
    expect(tableExportFilename('webp', at)).toBe('model-comparison_table_20260912_183012.webp');
  });

  // -------------------------------------------------------------------------------------------
  // The spreadsheet
  // -------------------------------------------------------------------------------------------

  it('writes one sheet of twenty-four columns and a header row, and a second of provenance', async () => {
    const captured = captureXlsx();

    const blob = await toXlsx(model([buildEntry(), buildEntry({ key: 'run:2', sourceId: 44 })]));

    expect(captured.sheets.length).toBe(2);
    expect(captured.sheets[0].sheet).toBe('Comparison');
    // A frozen header, so twenty-four columns stay identifiable after a scroll.
    expect(captured.sheets[0].stickyRowsCount).toBe(1);
    expect(captured.sheets[0].data.length).toBe(3);
    expect(captured.sheets[0].data.every((row: unknown[]) => row.length === 24)).toBeTrue();
    expect(captured.sheets[1].sheet).toBe('Provenance');

    expect(blob.size).toBeGreaterThan(0);
    // The media type is what a consumer dispatches on, so it is set here rather than trusted.
    expect(blob.type).toBe(XLSX_MEDIA_TYPE);
  });

  it('types the spreadsheet cells so a reader can sort and sum them', async () => {
    const captured = captureXlsx();

    await toXlsx(model());

    const columnAt = (key: string): number =>
      COMPARISON_TABLE_COLUMNS.findIndex(column => column.key === key);
    const row = captured.sheets[0].data[1];

    expect(row[columnAt('costPerQuestion')].type).toBe(Number);
    expect(row[columnAt('costPerQuestion')].format).toBe('$0.0000');
    expect(row[columnAt('intelligenceIndex')].format).toBe('0.0');
    expect(row[columnAt('ttftP50Ms')].format).toBe('#,##0');
    expect(row[columnAt('speedIndexSaturated')].type).toBe(Boolean);
    expect(row[columnAt('label')].type).toBe(String);
  });

  // -------------------------------------------------------------------------------------------
  // The image
  // -------------------------------------------------------------------------------------------

  it('composes the table onto an opaque figure ground', () => {
    const canvas = composeTableImage(model(), { scale: 2 });

    expect(canvas.width).toBeGreaterThan(0);
    expect(canvas.height).toBeGreaterThan(0);
    const pixel = canvas.getContext('2d')!.getImageData(0, 0, 1, 1).data;
    expect(pixel[3]).toBe(255);
  });

  it('keeps both sides inside the dimension cap however much text a row carries', () => {
    // Twenty-four columns of unbounded prose over a large set: the widest columns wrap further
    // rather than the image being written at a size no format is worth encoding.
    const wordy = Array.from({ length: 30 }, (_unused, index) =>
      buildEntry({
        key: `run:${index + 1}`,
        sourceId: index + 1,
        label: `Model ${index + 1} with a deliberately long configured display name`,
        explanation: 'Not comparable: the scoring method version differs from the baseline. '.repeat(8)
      })
    );

    const canvas = composeTableImage(model(wordy), { scale: 2 });

    expect(canvas.width).toBeLessThanOrEqual(FIGURE_EXPORT_MAX_DIMENSION);
    expect(canvas.height).toBeLessThanOrEqual(FIGURE_EXPORT_MAX_DIMENSION);
  });
});
