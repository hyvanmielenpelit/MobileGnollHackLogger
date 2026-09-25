import { FIGURE_EXPORT_MAX_DIMENSION } from './figure-export';
import { DEFAULT_APPEARANCE_STYLE } from './figure-style';
import type { FigureAppearanceStyle } from './figure-style';
import { resolveFigureTheme } from './figure-theme';
import type { BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import {
  COMPARISON_TABLE_COLUMNS,
  ComparisonTableColumn,
  ComparisonTableModel,
  ComparisonTableProvenance,
  DEFAULT_TABLE_COLUMNS,
  TABLE_DISPLAY_COLUMNS,
  TABLE_IMAGE_SCALE,
  TableColumnConfig,
  TableImageSize,
  XLSX_MEDIA_TYPE,
  buildComparisonTableModel,
  comparisonTableCells,
  composeTableImage,
  encodeComparisonTable,
  formatUsdText,
  measureTableImage,
  normalizeTableColumnConfig,
  populatedColumnKeys,
  readingCellText,
  resolveTableImageLayout,
  sameTableColumnConfig,
  shownTableColumns,
  tableClipboardPayload,
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
        examItemCount: 18,
        unscoredItemCount: 0,
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
        caveat: 'Timings were recorded under parallel question execution.',
        modelTimeMeanMs: 28400,
        totalModelTimePerRunMeanMs: 511200,
        totalModelTimeSdMs: 21300
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

  /** Every display column shown, in catalogue order: in the data flavour, all twenty-six parts. */
  const ALL_COLUMNS: TableColumnConfig = {
    order: TABLE_DISPLAY_COLUMNS.map(column => column.key),
    shown: TABLE_DISPLAY_COLUMNS.map(column => column.key)
  };

  function model(
    entries: BenchmarkModelComparisonEntryDto[] = [buildEntry()],
    overrides: Partial<ComparisonTableProvenance> = {}
  ): ComparisonTableModel {
    return buildComparisonTableModel(entries, provenance(overrides), ALL_COLUMNS, 'data');
  }

  function columnIndex(built: ComparisonTableModel, key: string): number {
    return built.columns.findIndex(column => column.key === key);
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

  it('declares twenty-six columns, and a cell for every one of them on every row', () => {
    const built = model([buildEntry(), buildEntry({ key: 'run:2', sourceId: 44 })]);

    expect(built.columns.length).toBe(26);
    expect(new Set(built.columns.map(column => column.key)))
      .toEqual(new Set(COMPARISON_TABLE_COLUMNS.map(column => column.key)));
    expect(built.rows.length).toBe(2);
    for (const row of built.rows) {
      expect(COMPARISON_TABLE_COLUMNS.every(column => column.key in row.cells)).toBeTrue();
    }
  });

  it('carries the mean model time and the suite total model time, in milliseconds', () => {
    const [row] = model().rows;

    expect(row.cells['modelTimeMeanMs'].raw).toBe(28400);
    // Ten seconds and over reads in seconds, exactly as the on-screen table prints it.
    expect(row.cells['modelTimeMeanMs'].text).toBe('28.4 s');
    expect(row.cells['totalModelTimeMs'].raw).toBe(511200);
    // Ten seconds and over reads in seconds, exactly as the on-screen table prints it.
    expect(row.cells['totalModelTimeMs'].text).toBe('511.2 s');

    const columnKinds = new Map(COMPARISON_TABLE_COLUMNS.map(column => [column.key, column.kind]));
    expect(columnKinds.get('modelTimeMeanMs')).toBe('ms');
    expect(columnKinds.get('totalModelTimeMs')).toBe('ms');
  });

  it('carries the machine value beside the on-screen text in every cell', () => {
    const [row] = model().rows;

    expect(row.cells['costPerQuestion'].raw).toBe(0.0432);
    // Four decimal places always, exactly as the on-screen table prints it.
    expect(row.cells['costPerQuestion'].text).toBe('$0.0432');
    expect(formatUsdText(0.0043)).toBe('$0.0043');
    expect(formatUsdText(0)).toBe('$0.0000');
    expect(formatUsdText(2.035)).toBe('$2.0350');
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
    expect(row.cells['modelTimeMeanMs'].raw).toBeNull();
    expect(row.cells['modelTimeMeanMs'].text).toBe('—');
    expect(row.cells['totalModelTimeMs'].raw).toBeNull();
    expect(row.cells['costPerQuestion'].raw).toBeNull();
    expect(row.cells['pricingResolved'].raw).toBeNull();
    expect(row.cells['state'].text).toBe('Excluded');
    expect(row.cells['differsOn'].text).toBe('ScoringMethodVersion');
  });

  it('writes the columns in the configuration\'s order, not the catalogue\'s', () => {
    const config: TableColumnConfig = { order: ['runs', 'model', ...DEFAULT_TABLE_COLUMNS.order.slice(2)], shown: ['model', 'runs'] };
    const built = buildComparisonTableModel([buildEntry()], provenance(), config, 'reading');

    expect(built.columns.map(column => column.key)).toEqual(['runs', 'model']);
    const raw = toCsv(built);
    const headerLine = (raw.charCodeAt(0) === 0xfeff ? raw.slice(1) : raw).split('\r\n')[0];
    expect(headerLine).toBe('R,Model');

    const data = buildComparisonTableModel([buildEntry()], provenance(), config, 'data');
    expect(data.columns.map(column => column.key)).toEqual(['runCount', 'label', 'thinkingLevel', 'provider', 'source']);
  });

  it('reports the display columns holding at least one non-absent part', () => {
    const [row] = model().rows;

    expect(row.cells['scheduledChange'].raw).toBeNull();
    expect(row.cells['differsOn'].raw).toBeNull();
    expect(row.cells['speedIndexSaturated'].raw).toBeFalse();

    const keys = populatedColumnKeys(model().rows.map(r => r.cells));

    // A fully comparable, unscheduled entry leaves these two absent on every row.
    expect(keys).not.toContain('scheduledChange');
    expect(keys).not.toContain('differsOn');
    // A false boolean is not absent, so it stays populated.
    expect(keys).toContain('speedIndexSaturated');
    // A combined column is populated when any one of its parts is.
    expect(keys).toContain('notes');
    expect(populatedColumnKeys([comparisonTableCells(buildEntry({ table: null }))])).not.toContain('notes');
  });

  // -------------------------------------------------------------------------------------------
  // The display columns and the two flavours
  // -------------------------------------------------------------------------------------------

  it('catalogues 28 display columns that cover every part, each primary once and each other part as its own column', () => {
    expect(TABLE_DISPLAY_COLUMNS.length).toBe(28);
    expect(new Set(TABLE_DISPLAY_COLUMNS.map(column => column.key)).size).toBe(28);
    const singles = TABLE_DISPLAY_COLUMNS.filter(column => column.renderer === 'text');
    expect(singles.length).toBe(20);
    expect(singles.every(column => column.parts.length === 1 && column.parts[0] === column.key && column.sortPart === column.key)).toBeTrue();
    const primaries = TABLE_DISPLAY_COLUMNS.filter(column => column.renderer !== 'text').map(column => column.parts[0]);
    const covered = new Set([...singles.map(column => column.key), ...primaries.filter(part => !singles.some(s => s.key === part))]);
    expect(covered).toEqual(new Set(COMPARISON_TABLE_COLUMNS.map(column => column.key)));
    expect(TABLE_DISPLAY_COLUMNS.find(column => column.key === 'notes')!.sortPart).toBeNull();
  });

  it('defaults to the eight on-screen columns in today\'s order, the rest hidden', () => {
    expect(shownTableColumns(DEFAULT_TABLE_COLUMNS).map(column => column.header))
      .toEqual(['Model', 'R', 'State', 'Intelligence Index', 'Speed Index', 'Timings', 'Candidate $ / question', 'Notes']);
    expect(DEFAULT_TABLE_COLUMNS.order.length).toBe(28);
  });

  it('combines a column\'s parts in the reading flavour, worded as on screen', () => {
    const built = buildComparisonTableModel([buildEntry({ runCount: 1, reasoningMode: 'extended' })], provenance(), DEFAULT_TABLE_COLUMNS, 'reading');
    const cells = built.rows[0].cells;

    expect(built.columns.length).toBe(8);
    expect(cells['model'].text).toBe('Gemini 2.5 Flash · medium · extended · Google · Run 43');
    expect(cells['runs'].text).toBe('1 · n = 1');
    expect(cells['stateCol'].text).toBe('Comparable · Comparable with the baseline on every must-match key.');
    expect(cells['intelligence'].text).toBe('68.0 ± 6.4 · item sampling and reproducibility');
    expect(cells['speedIndexCol'].text).toBe('88.0');
    expect(cells['timings'].text).toBe('Model 28.4 s · Suite 511.2 s · TTFT 4200 ms / 12.5 s');
    expect(cells['cost'].text).toBe('$0.0432');
    expect(cells['notes'].text).toBe('2 unstable items');
  });

  it('leaves a part out of its combined cell while the part is shown as its own column', () => {
    const config: TableColumnConfig = {
      order: DEFAULT_TABLE_COLUMNS.order,
      shown: [...DEFAULT_TABLE_COLUMNS.shown, 'intervalHalfWidth', 'ttftP90Ms', 'provider']
    };
    const cells = buildComparisonTableModel([buildEntry()], provenance(), config, 'reading').rows[0].cells;
    const shown = new Set(config.shown);

    expect(cells['intelligence'].text).toBe('68.0 · item sampling and reproducibility');
    expect(cells['timings'].text).toBe('Model 28.4 s · Suite 511.2 s · TTFT P50 4200 ms');
    expect(cells['model'].text).toBe('Gemini 2.5 Flash · medium · Run 43');
    expect(readingCellText(TABLE_DISPLAY_COLUMNS[3], comparisonTableCells(buildEntry()), shown)).toBe(cells['intelligence'].text);
    expect(cells['intervalHalfWidth'].text).toBe('6.4');
  });

  it('expands every shown column into its parts in the data flavour, writing no part twice', () => {
    const config: TableColumnConfig = {
      order: [...DEFAULT_TABLE_COLUMNS.order.filter(key => key !== 'intervalHalfWidth').slice(0, 3), 'intervalHalfWidth',
        ...DEFAULT_TABLE_COLUMNS.order.filter(key => key !== 'intervalHalfWidth').slice(3)],
      shown: [...DEFAULT_TABLE_COLUMNS.shown, 'intervalHalfWidth']
    };
    const keys = buildComparisonTableModel([buildEntry()], provenance(), config, 'data').columns.map(column => column.key);

    expect(new Set(keys).size).toBe(keys.length);
    // ± is shown before Intelligence Index, so it is written there, and not again inside the index's parts.
    expect(keys.indexOf('intervalHalfWidth')).toBeLessThan(keys.indexOf('intelligenceIndex'));
    expect(keys.slice(0, 4)).toEqual(['label', 'thinkingLevel', 'provider', 'source']);
    expect(buildComparisonTableModel([buildEntry()], provenance(), DEFAULT_TABLE_COLUMNS, 'data').columns.length).toBe(23);
  });

  it('repairs a stored configuration: unknown keys dropped, missing ones appended hidden, the model always shown', () => {
    const repaired = normalizeTableColumnConfig({
      order: ['notes', 'nope', 'model', 'notes', 7],
      shown: ['notes', 'nope']
    });
    expect(repaired.order.slice(0, 2)).toEqual(['notes', 'model']);
    expect(repaired.order.length).toBe(28);
    expect(repaired.shown).toEqual(['notes', 'model']);
    expect(normalizeTableColumnConfig(null)).toEqual({ order: DEFAULT_TABLE_COLUMNS.order, shown: DEFAULT_TABLE_COLUMNS.shown });
    expect(sameTableColumnConfig(normalizeTableColumnConfig('x'), DEFAULT_TABLE_COLUMNS)).toBeTrue();
    expect(sameTableColumnConfig(repaired, DEFAULT_TABLE_COLUMNS)).toBeFalse();
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
    const built = model([buildEntry({ explanation: 'before\tafter' })]);
    const text = toTsv(built);

    expect(text.startsWith('\uFEFF')).toBeTrue();
    const columns = dataLines(text)[0].split('\t');
    expect(columns.length).toBe(26);
    expect(columns[columnIndex(built, 'explanation')]).toBe('before after');
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
    expect(rows.map(row => row.split(' | ').length)).toEqual([26, 26, 26]);
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

    expect(parsed.columns.length).toBe(26);
    expect(parsed.columns.map(column => column.key))
      .toEqual(model().columns.map(column => column.key));
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

  it('writes one sheet of twenty-six columns and a header row, and a second of provenance', async () => {
    const captured = captureXlsx();

    const blob = await toXlsx(model([buildEntry(), buildEntry({ key: 'run:2', sourceId: 44 })]));

    expect(captured.sheets.length).toBe(2);
    expect(captured.sheets[0].sheet).toBe('Comparison');
    // A frozen header, so twenty-six columns stay identifiable after a scroll.
    expect(captured.sheets[0].stickyRowsCount).toBe(1);
    expect(captured.sheets[0].data.length).toBe(3);
    expect(captured.sheets[0].data.every((row: unknown[]) => row.length === 26)).toBeTrue();
    expect(captured.sheets[1].sheet).toBe('Provenance');

    expect(blob.size).toBeGreaterThan(0);
    // The media type is what a consumer dispatches on, so it is set here rather than trusted.
    expect(blob.type).toBe(XLSX_MEDIA_TYPE);
  });

  it('types the spreadsheet cells so a reader can sort and sum them', async () => {
    const captured = captureXlsx();

    const built = model();
    await toXlsx(built);

    const columnAt = (key: string): number => columnIndex(built, key);
    const row = captured.sheets[0].data[1];

    expect(row[columnAt('costPerQuestion')].type).toBe(Number);
    expect(row[columnAt('costPerQuestion')].format).toBe('$0.0000');
    expect(row[columnAt('intelligenceIndex')].format).toBe('0.0');
    expect(row[columnAt('ttftP50Ms')].format).toBe('#,##0');
    expect(row[columnAt('modelTimeMeanMs')].format).toBe('#,##0');
    expect(row[columnAt('totalModelTimeMs')].format).toBe('#,##0');
    expect(row[columnAt('speedIndexSaturated')].type).toBe(Boolean);
    expect(row[columnAt('label')].type).toBe(String);
  });

  // -------------------------------------------------------------------------------------------
  // The image
  // -------------------------------------------------------------------------------------------

  it('composes the table onto an opaque figure ground', () => {
    const canvas = composeTableImage(model());

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

    const canvas = composeTableImage(model(wordy));

    expect(canvas.width).toBeLessThanOrEqual(FIGURE_EXPORT_MAX_DIMENSION);
    expect(canvas.height).toBeLessThanOrEqual(FIGURE_EXPORT_MAX_DIMENSION);
  });

  describe('image size, theme and row style', () => {
    /** The image's own model: today's eight columns, combined as on screen. */
    function readingModel(count = 3): ComparisonTableModel {
      const entries = Array.from({ length: count }, (_unused, index) =>
        buildEntry({ key: `run:${index + 1}`, sourceId: 43 + index }));
      return buildComparisonTableModel(entries, provenance(), DEFAULT_TABLE_COLUMNS, 'reading');
    }

    /**
     * Ten one-letter columns over one-digit cells: every column is squeezed up to the minimum width,
     * so no column cap binds and the table's width is the same under all of them.
     */
    function narrowModel(): ComparisonTableModel {
      const columns: ComparisonTableColumn[] = 'abcdefghij'.split('').map(key => ({ key, header: key.toUpperCase(), kind: 'text' as const }));
      const rows = [0, 1, 2].map(index => ({
        cells: Object.fromEntries(columns.map(column => [column.key, { raw: String(index), text: String(index) }]))
      }));
      return { columns, rows, provenance: provenance({ notices: [] }) };
    }

    function box(widthPx: number, heightPx: number, textScale = 1, density = 1): TableImageSize {
      return { mode: 'box', widthPx, heightPx, density, textScale };
    }

    function themed(patch: Partial<FigureAppearanceStyle>) {
      return resolveFigureTheme({ ...DEFAULT_APPEARANCE_STYLE, ...patch });
    }

    function pixelAt(canvas: HTMLCanvasElement, x: number, y: number): number[] {
      return Array.from(canvas.getContext('2d')!.getImageData(x, y, 1, 1).data);
    }

    /** The number of bytes in which two same-sized canvases differ. */
    function differingBytes(a: HTMLCanvasElement, b: HTMLCanvasElement): number {
      const left = a.getContext('2d')!.getImageData(0, 0, a.width, a.height).data;
      const right = b.getContext('2d')!.getImageData(0, 0, b.width, b.height).data;
      let count = 0;
      for (let index = 0; index < left.length; index++) {
        if (left[index] !== right[index]) {
          count++;
        }
      }
      return count;
    }

    it('draws Fit the table at 200 % when no options are given, at the natural size times two', () => {
      const built = readingModel();
      const implicit = composeTableImage(built);
      const explicit = composeTableImage(built, { size: { mode: 'fit', density: 2 } });
      const { layout, refusal } = resolveTableImageLayout(built);

      expect(TABLE_IMAGE_SCALE).toBe(2);
      expect(refusal).toBeNull();
      expect(layout!.scale).toBe(2);
      expect(layout!.columnCount).toBe(8);
      expect(layout!.rowCount).toBe(3);
      expect(implicit.width).toBe(layout!.pixelWidth);
      expect(implicit.height).toBe(layout!.pixelHeight);
      expect(layout!.pixelWidth).toBe(Math.floor(layout!.layoutWidth * 2));
      expect(layout!.pixelHeight).toBe(Math.floor(layout!.layoutHeight * 2));
      expect(explicit.width).toBe(implicit.width);
      expect(explicit.height).toBe(implicit.height);
      expect(differingBytes(implicit, explicit)).toBe(0);

      // The Fit the table information is that same natural width, in whole requested px.
      const natural = measureTableImage(built, { textScale: 1 });
      expect(natural.widthPx).toBeGreaterThanOrEqual(layout!.layoutWidth - 1e-6);
      expect(natural.widthPx).toBeLessThan(layout!.layoutWidth + 1);
    });

    it('paints the dark #181818 ground, opaque, by default', () => {
      const canvas = composeTableImage(readingModel());

      expect(pixelAt(canvas, 0, 0)).toEqual([0x18, 0x18, 0x18, 255]);
      expect(pixelAt(canvas, canvas.width - 1, canvas.height - 1)).toEqual([0x18, 0x18, 0x18, 255]);
    });

    it('paints the light theme\'s white ground, and nothing under a transparent background', () => {
      const light = composeTableImage(readingModel(), { theme: themed({ theme: 'light' }) });
      const transparent = composeTableImage(readingModel(), { theme: themed({ background: 'transparent' }) });

      expect(pixelAt(light, 0, 0)).toEqual([255, 255, 255, 255]);
      expect(pixelAt(transparent, 0, 0)[3]).toBe(0);
    });

    it('changes the drawn rows when the bands are turned off or the rules on', () => {
      const built = readingModel();
      const banded = composeTableImage(built);
      const plain = composeTableImage(built, { tableStyle: { rowBands: false, rowRules: false } });
      const ruled = composeTableImage(built, { tableStyle: { rowBands: true, rowRules: true } });

      expect(plain.width).toBe(banded.width);
      expect(plain.height).toBe(banded.height);
      expect(ruled.height).toBe(banded.height);
      expect(differingBytes(banded, plain)).toBeGreaterThan(0);
      expect(differingBytes(banded, ruled)).toBeGreaterThan(0);
    });

    it('lays a box out to exactly its bitmap, the table spanning its width', () => {
      const built = readingModel();
      const natural = measureTableImage(built, { textScale: 1 });
      const widthPx = natural.widthPx + 200;
      const heightPx = natural.heightPx + 100;

      const fills: { x: number; width: number }[] = [];
      const realFillRect = CanvasRenderingContext2D.prototype.fillRect;
      spyOn(CanvasRenderingContext2D.prototype, 'fillRect').and.callFake(function (
        this: CanvasRenderingContext2D,
        ...args: any[]
      ) {
        fills.push({ x: args[0], width: args[2] });
        return (realFillRect as any).apply(this, args);
      } as any);

      const { layout, refusal } = resolveTableImageLayout(built, { size: box(widthPx, heightPx, 1, 2) });
      const canvas = composeTableImage(built, { size: box(widthPx, heightPx, 1, 2) });

      expect(refusal).toBeNull();
      expect(layout!.pixelWidth).toBe(widthPx * 2);
      expect(layout!.pixelHeight).toBe(heightPx * 2);
      expect(layout!.scale).toBe(2);
      expect(canvas.width).toBe(widthPx * 2);
      expect(canvas.height).toBe(heightPx * 2);
      // The row band spans the whole box less its padding, so the columns took the spare width.
      const band = fills.find(fill => fill.x === 20);
      expect(band).toBeDefined();
      expect(band!.width).toBeCloseTo(widthPx - 40, 6);
    });

    it('refuses a box too narrow for the columns, naming a width that fits', () => {
      const built = readingModel();

      const { layout, refusal } = resolveTableImageLayout(built, { size: box(320, 4000) });

      expect(layout).toBeNull();
      const named = /^The 8 selected columns need at least (\d+) px of width at this text size\. Choose fewer columns in the Table tab, a width of (\d+) px or more, or a smaller text size\.$/.exec(refusal!);
      expect(named).not.toBeNull();
      const minimum = Number(named![1]);
      expect(Number(named![2])).toBe(minimum);
      expect(resolveTableImageLayout(built, { size: box(minimum, 4000) }).refusal).toBeNull();
      expect(resolveTableImageLayout(built, { size: box(minimum - 1, 4000) }).refusal).toContain('selected columns');
    });

    it('refuses a box too short for the rows, naming a height that fits', () => {
      const built = readingModel();
      const natural = measureTableImage(built, { textScale: 1 });

      const { layout, refusal } = resolveTableImageLayout(built, { size: box(natural.widthPx, 100) });

      expect(layout).toBeNull();
      expect(refusal).toBe(
        `The table's 3 rows need at least ${natural.heightPx} px of height at this width and text size. ` +
        `Choose a height of ${natural.heightPx} px or more, a smaller text size, or Fit the table.`
      );
    });

    it('refuses a box whose bitmap the browser could not allocate', () => {
      const { layout, refusal } = resolveTableImageLayout(readingModel(), { size: box(8000, 8000, 1, 3) });

      expect(layout).toBeNull();
      expect(refusal).toContain('24000 × 24000');
    });

    it('fits the measured size typed back in, and refuses one pixel less in height', () => {
      const built = readingModel();
      for (const textScale of [1, 1.25, 1.5]) {
        const { widthPx, heightPx } = measureTableImage(built, { textScale });
        const context = `at ${textScale}`;

        const fitted = resolveTableImageLayout(built, { size: box(widthPx, heightPx, textScale) });
        expect(fitted.refusal).withContext(context).toBeNull();
        expect(fitted.layout!.pixelWidth).withContext(context).toBe(widthPx);
        expect(fitted.layout!.pixelHeight).withContext(context).toBe(heightPx);
        expect(resolveTableImageLayout(built, { size: box(widthPx, heightPx - 1, textScale) }).refusal)
          .withContext(context)
          .toContain(`need at least ${heightPx} px of height`);
      }
    });

    it('refuses one pixel less on either side of the measured size where no column cap binds', () => {
      const built = narrowModel();
      for (const textScale of [1, 1.5]) {
        const { widthPx, heightPx } = measureTableImage(built, { textScale });
        const context = `at ${textScale}`;

        expect(resolveTableImageLayout(built, { size: box(widthPx, heightPx, textScale) }).refusal)
          .withContext(context)
          .toBeNull();
        expect(resolveTableImageLayout(built, { size: box(widthPx - 1, heightPx, textScale) }).refusal)
          .withContext(context)
          .toContain(`need at least ${widthPx} px of width`);
        expect(resolveTableImageLayout(built, { size: box(widthPx, heightPx - 1, textScale) }).refusal)
          .withContext(context)
          .toContain(`need at least ${heightPx} px of height`);
      }
    });

    it('grows the measured size with the text size', () => {
      const built = readingModel();
      const base = measureTableImage(built, { textScale: 1 });
      const larger = measureTableImage(built, { textScale: 2 });

      expect(larger.widthPx).toBeGreaterThan(base.widthPx);
      expect(larger.heightPx).toBeGreaterThan(base.heightPx);
    });

    it('rasterises a preview at the given density without changing the composition', () => {
      const built = readingModel();
      const { layout } = resolveTableImageLayout(built);

      const preview = composeTableImage(built, { previewRaster: 1 });

      expect(preview.width).toBe(Math.round(layout!.layoutWidth));
      expect(preview.height).toBe(Math.round(layout!.layoutHeight));
    });

    it('returns a 1 × 1 canvas for a refused size, and the encoder rejects with the refusal', async () => {
      const built = readingModel();
      const size = box(320, 4000);

      const canvas = composeTableImage(built, { size });

      expect(canvas.width).toBe(1);
      expect(canvas.height).toBe(1);
      await expectAsync(encodeComparisonTable(built, 'png', { size })).toBeRejectedWithError(/selected columns/);
    });
  });

  describe('tableClipboardPayload', () => {
    function dataModel(entries: BenchmarkModelComparisonEntryDto[] = [buildEntry()]): ComparisonTableModel {
      return buildComparisonTableModel(entries, provenance(), DEFAULT_TABLE_COLUMNS, 'data');
    }

    it('copies Excel as TSV text and a bare HTML table of cells', () => {
      const built = dataModel([buildEntry({ label: '<b>Flash</b>' })]);

      const payload = tableClipboardPayload(built, 'xlsx');

      expect(payload.text.startsWith('﻿')).toBeFalse();
      expect(payload.text).toBe(toTsv(built).slice(1));
      expect(payload.html!.startsWith('<table')).toBeTrue();
      expect(payload.html).toContain('<th>Model</th>');
      expect(payload.html).toContain('&lt;b&gt;Flash&lt;/b&gt;');
      expect(payload.html).not.toContain('<b>');
      expect(payload.html).not.toContain('<html');
      // Numbers go over as numbers, which a spreadsheet can sum.
      expect(payload.html).toContain('<td>0.0432</td>');
    });

    it('copies HTML as the formatted document, with TSV as its text', () => {
      const built = dataModel();

      const payload = tableClipboardPayload(built, 'html');

      expect(payload.html).toBe(toHtml(built));
      expect(payload.text).toBe(toTsv(built).slice(1));
    });

    it('copies Markdown, CSV, TSV and JSON as text only, without the byte-order mark', () => {
      const built = dataModel();

      const csv = tableClipboardPayload(built, 'csv');
      const tsv = tableClipboardPayload(built, 'tsv');

      expect(csv.text.startsWith('﻿')).toBeFalse();
      expect(csv.text).toBe(toCsv(built).slice(1));
      expect(csv.html).toBeUndefined();
      expect(tsv.text.startsWith('﻿')).toBeFalse();
      expect(tsv.text).toBe(toTsv(built).slice(1));
      expect(tableClipboardPayload(built, 'md')).toEqual({ text: toMarkdown(built) });
      expect(tableClipboardPayload(built, 'json')).toEqual({ text: toJson(built) });
    });
  });
});
