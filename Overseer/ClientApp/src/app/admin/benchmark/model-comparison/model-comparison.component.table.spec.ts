import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import {
  ComparisonFigureCard,
  DOWNLOAD_SETTINGS_STORAGE_KEY,
  FIGURE_SIDEBAR_STORAGE_KEY,
  FigureSidebarTab,
  TABLE_COLUMNS_STORAGE_KEY,
  ModelComparisonComponent,
  sidebarTabForView
} from './model-comparison.component';
import { MAX_PLOTTED_ENTRIES } from './model-comparison-charts';
import type { FigureStyle } from './figure-style';
import { BenchmarkPanelDiagnosticsDto } from './model-comparison.models';
import { DEFAULT_TABLE_COLUMNS, TableColumnConfig, TableFileFormat, xlsxWriterModule } from './table-export';
import { FIGURE_SIZE_STORAGE_KEY, TABLE_IMAGE_SIZE_STORAGE_KEY } from './figure-size';
import {
  buildExcludedEntry, buildDto, render, showView, renderTable, refresh, comparableSet, textOf,
  setUpModelComparisonSpec, openSidebarTab, tableRowOf, setWithoutTotal, PRE_HARNESS_15, chooseRadio, openSingle,
  composePreview, withStyleDebounce, expectTabContract, captureSaves, withClipboard
} from './model-comparison.component.testing';

describe('ModelComparisonComponent', () => {
  let component: ModelComparisonComponent;
  let fixture: ComponentFixture<ModelComparisonComponent>;

  setUpModelComparisonSpec({
    get component() { return component; },
    set component(value) { component = value; },
    get fixture() { return fixture; },
    set fixture(value) { fixture = value; }
  }, { stubReportPackPanel: false });

  // -------------------------------------------------------------------------------------------
  // Table export, and the two clipboard paths
  // -------------------------------------------------------------------------------------------

  /** A stand-in for the dynamically imported spreadsheet writer, which the specs never really run. */
  function stubXlsxWriter(): void {
    vi.spyOn(xlsxWriterModule, 'load').mockResolvedValue({
      default: () => ({ toBlob: () => Promise.resolve(new Blob(['xlsx-bytes'])) })
    } as any);
  }

  /** The Comparison table row's table actions, present in both table views. */
  function tableActions(): { copy: HTMLButtonElement; download: HTMLButtonElement } {
    return {
      copy: fixture.debugElement.query(By.css('.mc-table-export .mc-table-copy')).nativeElement as HTMLButtonElement,
      download: fixture.debugElement.query(By.css('.mc-table-export .mc-table-download')).nativeElement as HTMLButtonElement
    };
  }

  function chooseTableFormat(format: TableFileFormat): void {
    component.onTableFormatChange(format);
    refresh();
  }

  /** The keys of the rows every write takes: filters applied, current order, all pages. */
  function tableOrder(): string[] {
    return component.entryTable.viewAll(component.entries).map(entry => entry.key);
  }

  /** The sort button of one Interactive table header, found by its label. */
  function headerButton(label: string): HTMLButtonElement {
    const button = fixture.debugElement.queryAll(By.css('table.mc-table thead th .gh-th-sort'))
      .map(candidate => candidate.nativeElement as HTMLButtonElement)
      .find(candidate => candidate.textContent?.trim() === label);
    expect(button, `the ${label} header`).toBeTruthy();
    return button!;
  }

  /** The shown headers of the Interactive table, in order. */
  function tableHeaders(): string[] {
    return fixture.debugElement.queryAll(By.css('table.mc-table thead tr:first-child th'))
      .map(header => (header.nativeElement as HTMLElement).textContent?.trim() ?? '');
  }

  /** A column configuration from the defaults, with columns shown, hidden or moved. */
  function columnsWith(change: { show?: string[]; hide?: string[]; order?: string[] }): TableColumnConfig {
    const order = change.order ?? [...DEFAULT_TABLE_COLUMNS.order];
    const shown = [...DEFAULT_TABLE_COLUMNS.shown, ...(change.show ?? [])].filter(key => !(change.hide ?? []).includes(key));
    return { order, shown };
  }

  it('offers seven table formats in the table views\' Download tab, Excel first, each with one line on it', () => {
    renderTable(buildDto(comparableSet(4)));
    openSidebarTab('download');

    const select = fixture.debugElement.query(By.css('#mc-table-format')).nativeElement as HTMLSelectElement;
    expect(Array.from(select.options).map(option => option.value))
      .toEqual(['xlsx', 'csv', 'tsv', 'md', 'json', 'html', 'image']);
    expect(Array.from(select.options).map(option => option.textContent?.trim()).pop()).toBe('Image (PNG or WebP)');
    expect(fixture.debugElement.query(By.css('label[for="mc-table-format"]'))).toBeTruthy();
    expect(textOf('#mc-table-format-hint')).toContain('Numbers stay numbers; a second sheet holds the provenance.');
    // Settings and the scope line only: Copy table and Download table are on the Comparison table row.
    expect(fixture.debugElement.query(By.css('#mc-side-table-download'))).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-side-table-copy'))).toBeNull();
    expect(fixture.debugElement.query(By.css('#mc-side-panel-download .mc-table-scope'))).not.toBeNull();

    // The names follow the format select.
    select.value = 'csv';
    select.dispatchEvent(new Event('change'));
    refresh();
    expect(component.tableFormat).toBe('csv');
    expect(component.downloadTableName).toBe('Download the table as CSV');
    expect(tableActions().copy.getAttribute('aria-label')).toBe('Copy the table as CSV');

    // The suite and the pricing basis are in the wizard header; the meta line carries only the
    // computation time and the order.
    expect(textOf('.mc-table-computed')).toContain('Computed');
    expect(textOf('.mc-table-computed')).not.toContain('Current catalog');
    expect(component.tableProvenance.conditionSignature).toBe('9c79137965e4');
    // The inline export toolbar and the column dialog are gone.
    expect(fixture.debugElement.query(By.css('#mc-table-export-format'))).toBeNull();
    expect(fixture.debugElement.query(By.css('dialog:not(.mc-about-dialog)'))).toBeNull();
  });

  it('puts Copy table and Download table on the Comparison table row of both table views, each with an interest tooltip', () => {
    renderTable(buildDto(comparableSet(4)));

    const { copy, download } = tableActions();
    expect(copy.closest('.mc-table-toolbar')).not.toBeNull();
    expect(copy.classList).toContain('action-btn');
    expect(copy.getAttribute('aria-label')).toBe('Copy the table as cells for Excel');
    expect(download.classList).toContain('action-btn');
    expect(download.getAttribute('aria-label')).toBe(component.downloadTableName);
    expect(download.textContent?.trim()).toBe('');
    for (const button of [copy, download]) {
      expect(button.getAttribute('type')).toBe('button');
      expect(button.hasAttribute('title')).toBe(false);
      const tipId = button.getAttribute('interestfor');
      expect(tipId).toBeTruthy();
      expect(button.getAttribute('style') ?? '').toMatch(new RegExp(`anchor-name:\\s*--${tipId}`));
      const tip = fixture.nativeElement.querySelector(`#${tipId}`) as HTMLElement;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(tip.getAttribute('style') ?? '').toMatch(new RegExp(`position-anchor:\\s*--${tipId}`));
    }
    expect(textOf('#mc-tip-table-copy')).toBe('Copy the table as cells for Excel');
    expect(textOf('#mc-tip-table-download')).toBe('Download the table as Excel (.xlsx)');
    expect(fixture.debugElement.query(By.css('.mc-all-download-all'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();

    showView('tablePreview');
    expect(tableActions().download.closest('#mc-fig-panel-tablePreview .mc-preview-toolbar')).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();

    showView('all');
    expect(fixture.debugElement.query(By.css('.mc-table-copy'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();
    expect(fixture.debugElement.query(By.css('.mc-all-toolbar .mc-all-download-all'))).not.toBeNull();

    showView('single');
    expect(fixture.debugElement.query(By.css('.mc-fig-actions'))).toBeNull();
  });

  it('opens the Interactive table on one compact header row and one meta line', () => {
    renderTable(buildDto(comparableSet(4)));
    const panel = fixture.debugElement.query(By.css('#mc-fig-panel-table')).nativeElement as HTMLElement;

    const toolbar = panel.querySelector('.mc-table-toolbar')!;
    expect(toolbar).not.toBeNull();
    expect(toolbar.querySelector('#mc-table-heading')?.textContent?.trim()).toBe('Comparison table');
    const tip = toolbar.querySelector('app-info-tip')!;
    expect(tip).not.toBeNull();
    expect(panel.querySelector('#mc-table-about-tip')?.textContent)
      .toContain('Every entry is listed here, charted or not. Point at a State badge to see why.');
    expect(panel.querySelector('table.mc-table')?.getAttribute('aria-describedby')).toBe('mc-table-about-tip');
    expect(toolbar.querySelector('.mc-table-export .mc-table-copy')).not.toBeNull();

    // The removed paragraphs: the lead note is in the tip, the download scope in the Download tab.
    const visibleText = Array.from(panel.querySelectorAll('p')).map(p => p.textContent ?? '').join(' ');
    expect(visibleText).not.toContain('Every entry is listed here');
    expect(panel.textContent).not.toContain('A download holds every row');
    expect(panel.querySelector('.mc-section-intro')).toBeNull();

    const meta = panel.querySelector('.mc-table-meta')!;
    expect(meta.querySelector('.mc-table-computed')?.textContent).toContain('Computed');
    expect(meta.querySelector('.mc-table-order-line')?.getAttribute('role')).toBe('status');
    expect(meta.querySelector('.mc-table-order-line')?.textContent).toContain('Rows follow the model order');

    headerButton('State').click();
    fixture.detectChanges();
    const useModelOrder = meta.querySelector<HTMLButtonElement>('.mc-use-model-order')!;
    expect(useModelOrder).not.toBeNull();
    expect(useModelOrder.classList).toContain('gh-filter-clear');
  });

  it('names Copy table after what it writes, in every format', () => {
    renderTable(buildDto(comparableSet(3)));
    const names: Record<TableFileFormat, string> = {
      xlsx: 'Copy the table as cells for Excel',
      csv: 'Copy the table as CSV',
      tsv: 'Copy the table as TSV',
      md: 'Copy the table as Markdown',
      json: 'Copy the table as JSON',
      html: 'Copy the table as a formatted table',
      image: 'Copy the table as an image'
    };
    for (const format of Object.keys(names) as TableFileFormat[]) {
      chooseTableFormat(format);
      expect(tableActions().copy.getAttribute('aria-label'), format).toBe(names[format]);
    }
    component.onExportFormatChange('webp');
    refresh();
    expect(tableActions().copy.getAttribute('aria-label')).toBe('Copy the table as an image (copied as PNG)');
  });

  it('marks both table actions aria-disabled, not disabled, with no entries, and both refuse', async () => {
    renderTable(buildDto([]));
    const saved = captureSaves();

    const { copy, download } = tableActions();
    for (const button of [copy, download]) {
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.disabled).toBe(false);
    }
    expect(component.downloadTableTooltip).toContain('Nothing to export');

    await component.downloadTable();
    await component.copyTable();
    expect(saved.names.length).toBe(0);
    expect(component.exportStatus).toBe('');
  });

  it('downloads in one click, through one write path, with no column dialog', async () => {
    renderTable(buildDto(comparableSet(3)));
    const download = vi.spyOn(component, 'downloadTable').mockResolvedValue();
    const copy = vi.spyOn(component, 'copyTable').mockResolvedValue();

    tableActions().download.click();
    tableActions().copy.click();

    expect(download).toHaveBeenCalledTimes(1);
    expect(copy).toHaveBeenCalledTimes(1);
    expect(fixture.debugElement.query(By.css('dialog:not(.mc-about-dialog)'))).toBeNull();
  });

  it('writes one file per table format, under the extension that format names', async () => {
    renderTable(buildDto(comparableSet(4)));
    const saved = captureSaves();
    stubXlsxWriter();

    for (const format of ['xlsx', 'csv', 'tsv', 'md', 'json', 'html'] as TableFileFormat[]) {
      chooseTableFormat(format);
      await component.downloadTable();
    }

    expect(saved.blobs.length).toBe(6);
    expect(saved.names.map(name => name.split('.').pop()))
      .toEqual(['xlsx', 'csv', 'tsv', 'md', 'json', 'html']);
    expect(saved.names.every(name => /^model-comparison_table_\d{8}_\d{6}\./.test(name))).toBe(true);
    expect(saved.blobs.every(blob => blob.size > 0)).toBe(true);
    expect(component.exportStatus).toContain('4 entries');
    expect(component.exportStatus)
      .toContain('current order (model order: Intelligence Index, descending), filters applied, all pages');
    expect(component.exporting).toBe(false);
  });

  it('writes a data format split into parts, and a reading format combined as on screen', async () => {
    renderTable(buildDto(comparableSet(3)));
    const saved = captureSaves();

    chooseTableFormat('csv');
    await component.downloadTable();
    const header = (await saved.blobs[0].text()).split('\r\n')[0];
    // The Timings column is written as its four typed parts; a hidden column is not written.
    expect(header).toContain('Model time mean ms');
    expect(header).toContain('Suite total ms');
    expect(header).toContain('TTFT P90 ms');
    expect(header).not.toContain('Timings');
    expect(header).not.toContain('Model id');
    expect(component.exportStatus).toMatch(/9 columns, written as \d+\./);
    expect(component.tableScopeLine).toMatch(/^3 entries \(filters applied, current order, all pages\) · 9 columns, written as \d+$/);

    chooseTableFormat('md');
    await component.downloadTable();
    const markdown = await saved.blobs[1].text();
    expect(markdown).toContain('Timings');
    expect(markdown).not.toContain('Model time mean ms');
    expect(component.exportStatus).toContain('9 columns.');
    expect(component.tableScopeLine).toBe('3 entries (filters applied, current order, all pages) · 9 columns');
  });

  it('writes the table image in the shared image format at the table image size, naming its pixels', async () => {
    renderTable(buildDto(comparableSet(2)));
    const saved = captureSaves();

    chooseTableFormat('image');
    await component.downloadTable();
    component.onExportFormatChange('webp');
    await component.downloadTable();

    expect(saved.names[0]).toMatch(/\.png$/);
    // A browser with no WebP encoder answers with a PNG, and the file is then named .png.
    expect(saved.names[1]).toMatch(/\.(webp|png)$/);
    expect(saved.blobs.every(blob => blob.size > 0)).toBe(true);
    expect(component.exportStatus).toMatch(/ at \d+ × \d+ px\./);
  });

  it('refuses a table image size the table does not fit, disabling both actions with the reason', async () => {
    renderTable(buildDto(comparableSet(3)));
    const saved = captureSaves();
    chooseTableFormat('image');
    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom', customWidthPx: 400, customHeightPx: 320 });
    await component.measureTableImageNow();
    refresh();

    expect(component.tableImageRefusal).toMatch(/need at least \d+ px/);
    expect(component.canExportTable).toBe(false);
    const { copy, download } = tableActions();
    expect(copy.getAttribute('aria-disabled')).toBe('true');
    expect(download.getAttribute('aria-disabled')).toBe('true');
    expect(textOf('#mc-tip-table-download')).toBe(component.tableImageRefusal);
    openSidebarTab('download');
    expect(textOf('#mc-side-panel-download .mc-export-error')).toContain(component.tableImageRefusal);

    await component.downloadTable();
    expect(saved.names.length).toBe(0);

    // Any other format writes whatever the image size says.
    chooseTableFormat('csv');
    expect(component.canExportTable).toBe(true);
  });

  it('copies cells with an HTML fallback for Excel, a formatted table for HTML, and text for Markdown', async () => {
    renderTable(buildDto(comparableSet(3)));
    const written: ClipboardItem[] = [];
    withClipboard({ write: (items: ClipboardItem[]) => { written.push(...items); return Promise.resolve(); } });

    await component.copyTable();
    expect(written.length).toBe(1);
    expect([...written[0].types].sort()).toEqual(['text/html', 'text/plain']);
    expect(await (await written[0].getType('text/plain')).text()).toContain('\t');
    expect(component.exportStatus).toBe('Copied 3 entries as cells for Excel — current order (model order: ' +
      'Intelligence Index, descending), filters applied, all pages.');

    chooseTableFormat('html');
    await component.copyTable();
    expect([...written[1].types].sort()).toEqual(['text/html', 'text/plain']);
    expect(await (await written[1].getType('text/html')).text()).toContain('<table');

    chooseTableFormat('md');
    await component.copyTable();
    expect(written[2].types).toEqual(['text/plain']);
    expect(await (await written[2].getType('text/plain')).text()).toContain('| Model |');
    expect(component.exportStatus).toContain('Copied 3 entries as Markdown');

    // CSV and TSV paste without the byte-order mark their files carry.
    chooseTableFormat('csv');
    await component.copyTable();
    expect((await (await written[3].getType('text/plain')).text()).startsWith('﻿')).toBe(false);
    expect(component.exporting).toBe(false);
  });

  it('falls back to writeText for a text payload where only it exists', async () => {
    renderTable(buildDto(comparableSet(3)));
    const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
    withClipboard({ writeText });

    chooseTableFormat('md');
    await component.copyTable();

    expect(writeText).toHaveBeenCalledTimes(1);
    expect(vi.mocked(writeText).mock.lastCall![0] as string).toContain('| Model |');
    expect(component.exportStatus).toContain('Copied 3 entries as Markdown');
  });

  // -------------------------------------------------------------------------------------------
  // Assessor panel diagnostics
  // -------------------------------------------------------------------------------------------

  const INTERACTION_LABEL =
    'total same-family preference of both members; does not measure the bias of the panel mean';
  const ASYMMETRY_LABEL =
    'estimated panel bias; valid only if the reference reader is neutral between these two families';

  /** Two candidate families graded by an OpenAI member A, an Anthropic member B and a Google reference reader. */
  function panelDiagnostics(overrides: Partial<BenchmarkPanelDiagnosticsDto> = {}): BenchmarkPanelDiagnosticsDto {
    return {
      applicable: true,
      notApplicableReason: null,
      memberALabel: 'GPT-4.1',
      memberAProvider: 'OpenAI',
      memberBLabel: 'Claude Sonnet 4',
      memberBProvider: 'Anthropic',
      referenceLabel: 'Gemini 2.5 Pro',
      referenceProvider: 'Google',
      entries: [
        {
          entryKey: 'run:1', entryLabel: 'GPT-5', candidateProvider: 'OpenAI',
          memberAIndex: 72, memberBIndex: 66, panelIndex: 69, referenceIndex: 68,
          rankA: 1, rankB: 2, rankPanel: 1, rankReference: 1
        },
        {
          entryKey: 'run:2', entryLabel: 'Claude Opus 4', candidateProvider: 'Anthropic',
          memberAIndex: 64, memberBIndex: 70, panelIndex: 67, referenceIndex: null,
          rankA: 2, rankB: 1, rankPanel: 2, rankReference: null
        }
      ],
      judgeDependentPairs: [
        {
          firstEntryKey: 'run:1', firstEntryLabel: 'GPT-5', secondEntryKey: 'run:2', secondEntryLabel: 'Claude Opus 4',
          description: 'Member A ranks GPT-5 above Claude Opus 4 (72 vs 64); member B ranks Claude Opus 4 above GPT-5 (70 vs 66).'
        }
      ],
      referenceDependentPairs: [],
      familyGaps: [
        {
          provider1: 'OpenAI',
          provider2: 'Anthropic',
          pairedQuestionCount: 18,
          insufficientData: false,
          isMemberProviderPair: true,
          gapA: { value: 8, ciLow: 3.25, ciHigh: 12.75 },
          gapB: { value: -4, ciLow: -9.5, ciHigh: 1.5 },
          gapPanel: { value: 2, ciLow: -1, ciHigh: 5 },
          gapRef: { value: 1, ciLow: -2.2, ciHigh: 4.3 },
          interactionContrast: { value: 12, ciLow: 6, ciHigh: 18 },
          interactionContrastLabel: INTERACTION_LABEL,
          asymmetryEstimate: { value: 1, ciLow: -2, ciHigh: 4 },
          asymmetryEstimateLabel: ASYMMETRY_LABEL
        }
      ],
      accusationAudit: [
        {
          member: 'A', memberProvider: 'OpenAI', candidateProvider: 'Anthropic', sameFamily: false,
          charges: 14, overturned: 5, upheld: 7, indeterminate: 2, overturnRate: 5 / 12
        },
        {
          member: 'B', memberProvider: 'Anthropic', candidateProvider: 'Anthropic', sameFamily: true,
          charges: 12, overturned: 3, upheld: 9, indeterminate: 0, overturnRate: 0.25
        }
      ],
      auditSummaries: [
        { member: 'A', memberProvider: 'OpenAI', familyOverturnGap: 0.12 },
        { member: 'B', memberProvider: 'Anthropic', familyOverturnGap: null }
      ],
      caveats: ['The published score is the panel mean.', 'The reference reader never scores.'],
      ...overrides
    };
  }

  function notApplicableDiagnostics(): BenchmarkPanelDiagnosticsDto {
    return {
      applicable: false,
      notApplicableReason: 'Every run must be a panel run; 1 run (12) had a single assessor.',
      entries: [],
      judgeDependentPairs: [],
      referenceDependentPairs: [],
      familyGaps: [],
      accusationAudit: [],
      auditSummaries: [],
      caveats: []
    };
  }

  /** The rendered diagnostics section, or null while none is rendered. */
  function panelSection(): HTMLElement | null {
    return (fixture.debugElement.query(By.css('#mc-fig-panel-table .mc-panel-diagnostics'))?.nativeElement as HTMLElement | undefined) ?? null;
  }

  /** One diagnostics table's body rows, each as its trimmed cell texts. */
  function panelRows(tableClass: string): string[][] {
    return Array.from(panelSection()!.querySelectorAll(`table.${tableClass} tbody tr`))
      .map(row => Array.from(row.querySelectorAll('th, td')).map(cell => cell.textContent?.trim() ?? ''));
  }

  it('renders no assessor panel diagnostics section when the comparison carries none', () => {
    renderTable(buildDto(comparableSet(2)));

    expect(fixture.debugElement.query(By.css('#mc-fig-panel-table table.mc-table'))).toBeTruthy();
    expect(panelSection()).toBeNull();
  });

  it('renders only the reason when the assessor panel diagnostics do not apply', () => {
    renderTable(buildDto(comparableSet(2), { panelDiagnostics: notApplicableDiagnostics() }));

    const section = panelSection()!;
    expect(section).not.toBeNull();
    expect(section.getAttribute('aria-labelledby')).toBe('mc-panel-heading');
    expect(section.querySelector('#mc-panel-heading')?.textContent?.trim()).toBe('Assessor panel diagnostics');
    const reason = section.querySelector('.mc-panel-na')!;
    expect(reason.textContent?.trim()).toBe('Every run must be a panel run; 1 run (12) had a single assessor.');
    expect(reason.getAttribute('role')).toBe('note');
    expect(section.querySelectorAll('table').length).toBe(0);
    expect(section.querySelector('.mc-panel-caveats')).toBeNull();
  });

  it('renders the graders, the indices by grader and the order-dependent pairs', () => {
    renderTable(buildDto(comparableSet(2), { panelDiagnostics: panelDiagnostics() }));
    const section = panelSection()!;

    expect(Array.from(section.querySelectorAll('.mc-panel-roles dd')).map(dd => dd.textContent?.trim()))
      .toEqual(['GPT-4.1 (OpenAI)', 'Claude Sonnet 4 (Anthropic)', 'Gemini 2.5 Pro (Google)']);
    expect(Array.from(section.querySelectorAll('table.mc-panel-indices thead th')).map(th => th.textContent?.trim()))
      .toEqual(['Entry', 'Provider', 'Member A', 'Member B', 'Panel', 'Reference']);
    expect(panelRows('mc-panel-indices')).toEqual([
      ['GPT-5', 'OpenAI', '72 (#1)', '66 (#2)', '69 (#1)', '68 (#1)'],
      ['Claude Opus 4', 'Anthropic', '64 (#2)', '70 (#1)', '67 (#2)', '—']
    ]);
    expect(section.querySelector('.mc-panel-judge-pairs li')?.textContent?.trim())
      .toBe('Member A ranks GPT-5 above Claude Opus 4 (72 vs 64); member B ranks Claude Opus 4 above GPT-5 (70 vs 66).');
    expect(section.querySelector('.mc-panel-reference-pairs-empty')?.textContent?.trim())
      .toBe('None: the panel and the reference reader order every pair of entries the same way.');
  });

  it('renders the family gaps with their intervals and both contrasts under their labels, verbatim', () => {
    renderTable(buildDto(comparableSet(2), { panelDiagnostics: panelDiagnostics() }));
    const section = panelSection()!;

    expect(panelRows('mc-panel-gaps')).toEqual([
      ['OpenAI − Anthropic', '18', '+8.0 [+3.3, +12.8]', '-4.0 [-9.5, +1.5]', '+2.0 [-1.0, +5.0]', '+1.0 [-2.2, +4.3]']
    ]);
    const interaction = section.querySelector('.mc-panel-interaction')!;
    expect(interaction.querySelector('dt')?.textContent?.trim()).toBe('Interaction contrast (OpenAI − Anthropic)');
    expect(interaction.querySelector('.mc-panel-contrast-value')?.textContent?.trim()).toBe('+12.0 [+6.0, +18.0]');
    expect(interaction.querySelector('.mc-panel-contrast-label')?.textContent?.trim()).toBe(INTERACTION_LABEL);
    const asymmetry = section.querySelector('.mc-panel-asymmetry')!;
    expect(asymmetry.querySelector('dt')?.textContent?.trim()).toBe('Asymmetry estimate (OpenAI − Anthropic)');
    expect(asymmetry.querySelector('.mc-panel-contrast-label')?.textContent?.trim()).toBe(ASYMMETRY_LABEL);
  });

  it('shows insufficient data on a flagged family gap row, and its contrasts', () => {
    const empty = { value: null, ciLow: null, ciHigh: null };
    const flagged = panelDiagnostics({
      familyGaps: [{
        ...panelDiagnostics().familyGaps[0],
        pairedQuestionCount: 3,
        insufficientData: true,
        gapA: empty, gapB: empty, gapPanel: empty, gapRef: empty, interactionContrast: empty, asymmetryEstimate: empty
      }]
    });
    renderTable(buildDto(comparableSet(2), { panelDiagnostics: flagged }));
    const section = panelSection()!;

    expect(panelRows('mc-panel-gaps')).toEqual([
      ['OpenAI − Anthropic', '3', 'insufficient data', 'insufficient data', 'insufficient data', 'insufficient data']
    ]);
    expect(section.querySelector('table.mc-panel-gaps tbody tr')?.classList).toContain('mc-panel-insufficient');
    expect(section.querySelector('.mc-panel-interaction .mc-panel-contrast-value')?.textContent?.trim())
      .toBe('insufficient data');
  });

  it('renders the accusation audit, each member\'s family overturn gap and the caveats', () => {
    renderTable(buildDto(comparableSet(2), { panelDiagnostics: panelDiagnostics() }));
    const section = panelSection()!;

    expect(panelRows('mc-panel-audit')).toEqual([
      ['Member A', 'OpenAI', 'Anthropic', 'Other family', '14', '5', '7', '2', '42%'],
      ['Member B', 'Anthropic', 'Anthropic', 'Same family', '12', '3', '9', '0', '25%']
    ]);
    expect(Array.from(section.querySelectorAll('.mc-panel-audit-summaries li')).map(li => li.textContent?.trim()))
      .toEqual([
        'Member A (OpenAI) family overturn gap: +12 pp',
        'Member B (Anthropic) family overturn gap: withheld: too few ruled charges'
      ]);
    expect(Array.from(section.querySelectorAll('.mc-panel-caveats li')).map(li => li.textContent?.trim()))
      .toEqual(['The published score is the panel mean.', 'The reference reader never scores.']);
  });

  it('renders server text in the diagnostics as plain text, never as markup', () => {
    renderTable(buildDto(comparableSet(2), {
      panelDiagnostics: panelDiagnostics({ caveats: ['<b>bold</b> <img src=x>'] })
    }));
    const caveat = panelSection()!.querySelector('.mc-panel-caveats li')!;

    expect(caveat.textContent?.trim()).toBe('<b>bold</b> <img src=x>');
    expect(caveat.querySelector('b')).toBeNull();
    expect(caveat.querySelector('img')).toBeNull();
  });

  it('leaves the comparison table\'s columns and rows as they are beside the diagnostics', () => {
    renderTable(buildDto(comparableSet(3), { panelDiagnostics: panelDiagnostics() }));

    expect(panelSection()).not.toBeNull();
    expect(tableHeaders().length).toBe(DEFAULT_TABLE_COLUMNS.shown.length);
    expect(tableHeaders()).toEqual(component.shownColumns.map(column => column.header));
    expect(fixture.debugElement.queryAll(By.css('table.mc-table tbody tr')).length).toBe(3);
  });

  it('copies Markdown with the diagnostics block when they apply, and without it when they do not', async () => {
    renderTable(buildDto(comparableSet(3), { panelDiagnostics: panelDiagnostics() }));
    const writeText = vi.fn().mockName('writeText').mockResolvedValue(undefined);
    withClipboard({ writeText });

    chooseTableFormat('md');
    await component.copyTable();
    const applied = vi.mocked(writeText).mock.lastCall![0] as string;
    expect(applied).toContain('| Model |');
    expect(applied).toContain('\n## Assessor Panel Diagnostics\n');
    expect(applied).toContain('| GPT-5 | OpenAI | 72 (#1) | 66 (#2) | 69 (#1) | 68 (#1) |');
    expect(applied.indexOf('| Model |')).toBeLessThan(applied.indexOf('## Assessor Panel Diagnostics'));

    fixture.componentRef.setInput('comparison', buildDto(comparableSet(3), { panelDiagnostics: notApplicableDiagnostics() }));
    fixture.detectChanges();
    await component.copyTable();
    expect(vi.mocked(writeText).mock.lastCall![0] as string).not.toContain('Assessor Panel Diagnostics');
  });

  it('copies the table image as a PNG, WebP chosen or not', async () => {
    renderTable(buildDto(comparableSet(2)));
    const written: ClipboardItem[] = [];
    withClipboard({ write: (items: ClipboardItem[]) => { written.push(...items); return Promise.resolve(); } });
    chooseTableFormat('image');
    component.onExportFormatChange('webp');

    await component.copyTable();

    expect(written.length).toBe(1);
    expect(written[0].types).toEqual(['image/png']);
    expect(component.exportStatus).toMatch(/^Copied 2 entries as an image \(PNG, \d+ × \d+ px\)/);

    withClipboard(undefined);
    await component.copyTable();
    expect(component.exportStatus).toBe('This browser cannot copy images — download the table instead.');
  });

  it('reports a refused clipboard write and an absent clipboard API inline rather than throwing', async () => {
    renderTable(buildDto(comparableSet(3)));
    withClipboard({
      write: () => Promise.reject(new Error('Document is not focused.')),
      writeText: () => Promise.reject(new Error('Document is not focused.'))
    });

    await expect(component.copyTable()).resolves.not.toThrow();
    expect(component.exportStatus).toBe('The clipboard write was refused.');

    withClipboard(undefined);
    await component.copyTable();
    expect(component.exportStatus).toContain('cannot copy text to the clipboard');
    expect(component.exportStatus).toContain('download the table instead');
    expect(component.exporting).toBe(false);
  });

  it('exports every filtered row across all pages, not the visible page', async () => {
    renderTable(buildDto(comparableSet(12)));
    const saved = captureSaves();
    expect(component.entryTable.view(component.entries).length).toBe(10);

    chooseTableFormat('csv');
    await component.downloadTable();

    // Twelve records and a header, from a page showing ten.
    const text = await saved.blobs[0].text();
    expect(text.trimEnd().split('\r\n').length).toBe(13);
    expect(component.exportStatus).toContain('12 entries');
  });

  it('exports the rows the column filters leave, and says how many', async () => {
    renderTable(buildDto(comparableSet(12)));
    const saved = captureSaves();
    // Model 1, Model 10, Model 11 and Model 12.
    component.entryTable.setFilter('label', 'Model 1');
    component.onTableChanged();

    chooseTableFormat('csv');
    await component.downloadTable();

    expect((await saved.blobs[0].text()).trimEnd().split('\r\n').length).toBe(5);
    expect(component.exportStatus).toContain('4 entries');
  });

  it('remembers the table format, the image format and the WebP quality per browser, and repairs a bad record', () => {
    localStorage.setItem(DOWNLOAD_SETTINGS_STORAGE_KEY, JSON.stringify({
      version: 1, tableFormat: 'md', imageFormat: 'webp', webpQuality: 90
    }));
    let stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableFormat).toBe('md');
    expect(stored.componentInstance.exportFormat).toBe('webp');
    expect(stored.componentInstance.webpQuality).toBe(90);
    stored.destroy();

    localStorage.setItem(DOWNLOAD_SETTINGS_STORAGE_KEY, JSON.stringify({
      version: 1, tableFormat: 'png', imageFormat: 'gif', webpQuality: 42
    }));
    stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableFormat).toBe('xlsx');
    expect(stored.componentInstance.exportFormat).toBe('png');
    expect(stored.componentInstance.webpQuality).toBe(85);
    stored.destroy();

    localStorage.setItem(DOWNLOAD_SETTINGS_STORAGE_KEY, '{not json');
    stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableFormat).toBe('xlsx');
    stored.destroy();

    component.onTableFormatChange('json');
    component.onWebpQualityChange(95);
    expect(JSON.parse(localStorage.getItem(DOWNLOAD_SETTINGS_STORAGE_KEY)!))
      .toEqual({ version: 1, tableFormat: 'json', imageFormat: 'png', webpQuality: 95 });
  });

  it('shows the table image size and the image format for Image, and always on the Table preview', async () => {
    renderTable(buildDto(comparableSet(3)));
    openSidebarTab('download');
    const shown = (): boolean[] => [
      fixture.debugElement.query(By.css('#mc-side-panel-download app-export-size-section')) !== null,
      fixture.debugElement.query(By.css('#mc-side-panel-download #mc-image-format-section')) !== null
    ];
    expect(shown()).toEqual([false, false]);

    chooseTableFormat('image');
    expect(shown()).toEqual([true, true]);
    const sizes = Array.from((fixture.debugElement.query(By.css('#mc-table-image-resolution'))
      .nativeElement as HTMLSelectElement).options).map(option => option.value);
    expect(sizes[0]).toBe('fit');

    chooseTableFormat('xlsx');
    showView('tablePreview');
    expect(component.effectiveSidebarTab).toBe('download');
    expect(shown()).toEqual([true, true]);
    // The image format here is the one the charts are written in.
    component.onExportFormatChange('webp');
    showView('all');
    refresh();
    await Promise.resolve();
    fixture.detectChanges();
    expect((fixture.debugElement.query(By.css('#mc-export-format')).nativeElement as HTMLSelectElement).value).toBe('webp');
  });

  it('shows the Fit the table information in custom mode only, and updates it with the columns', async () => {
    renderTable(buildDto(comparableSet(3)));
    openSidebarTab('download');
    chooseTableFormat('image');

    // Fit the table is the default, where the size is the table's own and the written size says so.
    await component.measureTableImageNow();
    expect(component.tableFitInfo).toBe('');
    expect(component.tableImageWrittenLabel).toMatch(/^\d+ × \d+ px — the whole table at 200%$/);

    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom' });
    await component.measureTableImageNow();
    refresh();
    const info = /^Fit the table: (\d+) × (\d+) px at this text size — the whole table with its (\d+) shown columns and 3 rows\.$/
      .exec(component.tableFitInfo);
    expect(info, component.tableFitInfo).not.toBeNull();
    expect(info![3]).toBe('9');
    expect(textOf('#mc-table-image-fit-info')).toBe(component.tableFitInfo);

    component.onTableColumnsChange(columnsWith({ hide: ['notes', 'timings'] }));
    await component.measureTableImageNow();
    const narrower = /^Fit the table: (\d+) × (\d+) px/.exec(component.tableFitInfo)!;
    expect(component.tableFitInfo).toContain('its 7 shown columns');
    expect(Number(narrower[1])).toBeLessThan(Number(info![1]));

    // A preset names its minimum in its refusal instead.
    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'fullhd' });
    await component.measureTableImageNow();
    expect(component.tableFitInfo).toBe('');
  });

  it('keeps the chart size and the table image size apart, each stored under its own key', () => {
    render(buildDto(comparableSet(3)), 2);
    const chart = component.figureSize;

    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom', customWidthPx: 1000, customHeightPx: 800 });
    expect(component.figureSize).toEqual(chart);
    expect(JSON.parse(localStorage.getItem(TABLE_IMAGE_SIZE_STORAGE_KEY)!).customWidthPx).toBe(1000);
    expect(localStorage.getItem(FIGURE_SIZE_STORAGE_KEY)).toBeNull();

    component.onExportResolutionChange('hd');
    expect(component.tableImageSize.customWidthPx).toBe(1000);
    expect(component.tableImageSize.resolutionId).toBe('custom');
  });

  // -------------------------------------------------------------------------------------------
  // The table's rows follow the model order
  // -------------------------------------------------------------------------------------------

  it('orders the table by the model order by default, with no header sorted and a line naming it', () => {
    renderTable(buildDto(comparableSet(3)));

    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(component.figures!.selection.plotted.map(entry => entry.key));
    expect(tableOrder()).toEqual(['run:3', 'run:2', 'run:1']);
    const sorted = fixture.debugElement.queryAll(By.css('table.mc-table thead th[aria-sort]'));
    expect(sorted.length).toBe(0);
    expect(textOf('.mc-table-order-line')).toContain('Rows follow the model order: Intelligence Index, descending');
    expect(fixture.debugElement.query(By.css('.mc-use-model-order'))).toBeNull();
  });

  it('sorts by a column header, says so, and returns to the model order with Use model order', () => {
    const entries = comparableSet(3);
    entries[1] = { ...entries[1], state: 'Degraded', speedDegraded: true, speedDegradingKeys: ['ParallelMode'] };
    renderTable(buildDto(entries));

    headerButton('State').click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('stateCol');
    // State is one click away, and puts the entries a reader has to check first.
    expect(tableOrder()[0]).toBe('run:2');
    expect(textOf('.mc-table-order-line')).toContain('Rows sorted by State');

    // The Data tab says so too, beside the table.
    expect(textOf('#mc-side-panel-data .mc-table-order--side')).toContain('The table is sorted by State.');

    (fixture.debugElement.query(By.css('.mc-use-model-order')).nativeElement as HTMLButtonElement).click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(component.entryTable.sortDirection).toBe('asc');
    expect(tableOrder()).toEqual(['run:3', 'run:2', 'run:1']);
    expect(fixture.debugElement.query(By.css('#mc-side-panel-data .mc-table-order--side'))).toBeNull();
  });

  it('returns the table to the model order on every model-order change, and keeps charts and table in one order', () => {
    renderTable(buildDto(comparableSet(3)));

    headerButton('Candidate $ / question').click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('cost');

    // The table views' Data tab carries the model order too.
    chooseRadio('mc-sort-key-cost');
    expect(component.sort.key).toBe('cost');
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(component.figures!.selection.plotted.map(entry => entry.key));

    headerButton('R').click();
    refresh();
    chooseRadio('mc-sort-direction-asc');
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(component.figures!.selection.plotted.map(entry => entry.key));
    expect(textOf('.mc-table-order-line')).toContain('Rows follow the model order: Cost, ascending');
  });

  it('names the order in the export toast: the model order, custom, or the sorted column', async () => {
    renderTable(buildDto(comparableSet(3)));
    captureSaves();
    chooseTableFormat('csv');

    headerButton('Candidate $ / question').click();
    refresh();
    await component.downloadTable();
    expect(component.exportStatus).toContain('current order (sorted by Candidate $ / question)');

    component.onSortKeyChange('custom');
    await component.downloadTable();
    expect(component.exportStatus).toContain('current order (model order: custom)');
  });

  // -------------------------------------------------------------------------------------------
  // The custom model order
  // -------------------------------------------------------------------------------------------

  it('seeds Custom from the order in effect, disables Direction, and keeps it across a switch of key', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-sort-direction-asc');
    expect(component.figures!.smallMultiples.order).toEqual(['run:1', 'run:2', 'run:3']);

    chooseRadio('mc-sort-key-custom');
    expect(component.sort.key).toBe('custom');
    expect(component.customOrder).toEqual(['run:1', 'run:2', 'run:3']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:1', 'run:2', 'run:3']);
    const direction = fixture.debugElement.queryAll(By.css('#mc-side-panel-data fieldset.gh-choice'))
      .map(group => group.nativeElement as HTMLFieldSetElement)
      .find(group => group.querySelector('legend')?.textContent?.trim() === 'Direction')!;
    expect(direction.disabled).toBe(true);
    expect(textOf('#mc-sort-direction-hint')).toContain('A custom order has no direction.');
    const list = fixture.debugElement.query(By.css('#mc-side-panel-data app-reorderable-list'));
    expect(list).toBeTruthy();
    expect((list.componentInstance as { items: readonly { key: string }[] }).items.map(item => item.key))
      .toEqual(['run:1', 'run:2', 'run:3']);

    component.onCustomOrderChange(['run:3', 'run:1', 'run:2']);
    chooseRadio('mc-sort-key-label');
    expect(component.sort.key).toBe('label');
    expect(fixture.debugElement.query(By.css('#mc-side-panel-data app-reorderable-list'))).toBeNull();
    chooseRadio('mc-sort-key-custom');
    expect(component.customOrder).toEqual(['run:3', 'run:1', 'run:2']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:3', 'run:1', 'run:2']);
  });

  it('reorders the charts and the table from one custom move, and returns a column sort to the model order', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-sort-key-custom');
    showView('table');
    headerButton('Model').click();
    refresh();
    expect(component.entryTable.sortColumn).toBe('model');
    const rebuild = vi.spyOn(component as unknown as {
      rebuild(): void;
    }, 'rebuild');

    // The Custom list is in the table views' Data tab too; a row moves through its handle's Move menu.
    const handle = fixture.debugElement.queryAll(By.css('#mc-side-panel-data app-reorderable-list button'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => button.getAttribute('aria-label') === 'Move Model 3');
    expect(handle, 'Move Model 3').toBeTruthy();
    handle!.click();
    refresh();
    const moveDown = fixture.nativeElement.querySelector('#mc-side-panel-data #mc-custom-order-move-down') as HTMLButtonElement | null;
    expect(moveDown, 'mc-custom-order-move-down').toBeTruthy();
    moveDown!.click();
    refresh();

    expect(rebuild).toHaveBeenCalledTimes(1);
    expect(component.customOrder).toEqual(['run:2', 'run:3', 'run:1']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:2', 'run:3', 'run:1']);
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(tableOrder()).toEqual(['run:2', 'run:3', 'run:1']);
    expect(textOf('.mc-table-order-line')).toContain('Rows follow the model order: custom');
  });

  it('resets the custom order to Intelligence Index, descending, and refuses while it already is', () => {
    render(buildDto(comparableSet(3)), 2);
    chooseRadio('mc-sort-key-custom');
    const reset = (): HTMLButtonElement => fixture.debugElement.queryAll(By.css('#mc-side-panel-data .mc-order-actions button'))
      .map(button => button.nativeElement as HTMLButtonElement)
      .find(button => button.textContent?.trim() === 'Reset custom order')!;
    expect(reset().getAttribute('aria-disabled')).toBe('true');

    component.onCustomOrderChange(['run:1', 'run:3', 'run:2']);
    refresh();
    expect(reset().getAttribute('aria-disabled')).toBeNull();

    reset().click();
    refresh();
    expect(component.customOrder).toEqual(['run:3', 'run:2', 'run:1']);
    expect(reset().getAttribute('aria-disabled')).toBe('true');
    reset().click();
    expect(component.customOrder).toEqual(['run:3', 'run:2', 'run:1']);
  });

  it('tags entries the charts never draw as table only, and draws the divider where the charts stop', () => {
    render(buildDto([...comparableSet(MAX_PLOTTED_ENTRIES + 1), buildExcludedEntry('run:99', ['ScoringMethodVersion'])]), 2);
    component.onSortKeyChange('custom');

    const tags = (key: string): readonly string[] =>
      component.customOrderItems.find(item => item.key === key)?.tags ?? [];
    expect(component.customOrderItems.length).toBe(MAX_PLOTTED_ENTRIES + 2);
    expect(tags('run:99')).toEqual(['table only']);
    expect(component.customOrderDividerIndex).toBe(MAX_PLOTTED_ENTRIES);

    component.toggleEntry('run:5');
    expect(tags('run:5')).toEqual(['table only']);
    // Now every chartable entry fits under the cap, so there is no line to draw.
    expect(component.customOrderDividerIndex).toBeNull();
  });

  it('keeps the custom order across a refetch, dropping gone entries and appending new ones by Intelligence Index', () => {
    const entries = comparableSet(4);
    render(buildDto(entries.slice(0, 3)), 2);
    component.onSortKeyChange('custom');
    component.onCustomOrderChange(['run:1', 'run:3', 'run:2']);

    fixture.componentRef.setInput('comparison', buildDto([entries[0], entries[1], entries[3]]));
    fixture.detectChanges();

    expect(component.customOrder).toEqual(['run:1', 'run:2', 'run:4']);
    expect(component.sort.customOrder).toEqual(['run:1', 'run:2', 'run:4']);
    expect(component.figures!.smallMultiples.order).toEqual(['run:1', 'run:2', 'run:4']);
  });

  // -------------------------------------------------------------------------------------------
  // The table's columns
  // -------------------------------------------------------------------------------------------

  it('renders today\'s table with the default columns', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], state: 'Degraded', speedDegraded: true, speedDegradingKeys: ['ParallelMode'] };
    renderTable(buildDto(entries));

    expect(tableHeaders()).toEqual([
      'Model', 'R', 'State', 'Intelligence Index', 'Speed Index', 'Timings', 'Candidate $ / question',
      'Total $ / run, with grading', 'Notes'
    ]);
    expect(fixture.debugElement.queryAll(By.css('table.mc-table thead th[app-sort-header]')).length).toBe(8);
    expect(textOf('table.mc-table caption')).toContain(
      'Model, R, State, Intelligence Index, Speed Index, Timings, Candidate $ / question, Total $ / run, with grading, Notes');

    const filters = fixture.debugElement.queryAll(By.css('.gh-filter-row td'))
      .map(cell => cell.nativeElement as HTMLElement);
    expect(filters.length).toBe(9);
    expect(filters[0].querySelector('#mc-f-label')).toBeTruthy();
    expect(filters[2].querySelector('#mc-f-state')).toBeTruthy();

    const row = tableRowOf('Run 1');
    expect(row.children.length).toBe(9);
    expect(row.children[0].tagName).toBe('TH');
    expect(row.children[1].classList).toContain('col-center');
    expect(row.querySelector('.mc-state')?.getAttribute('interestfor')).toBeTruthy();
    expect(row.querySelector('.mc-degraded-axes')?.textContent).toContain('Degraded: speed');
    expect(row.querySelector('.mc-interval')?.textContent).toContain('± 6.4');
    expect(row.querySelector('.mc-basis-btn')).toBeTruthy();
    expect(row.querySelectorAll('.mc-timings > div').length).toBe(3);
    expect(row.querySelector('.mc-notes')?.textContent).toContain('Speed degraded by: ParallelMode');
    expect(row.querySelector('.mc-source')?.textContent?.trim()).toBe('Run 1');
  });

  it('hides, shows and moves columns from the one configuration, and stores it', () => {
    renderTable(buildDto(comparableSet(2)));

    const order = [...DEFAULT_TABLE_COLUMNS.order];
    order.splice(order.indexOf('cost'), 1);
    order.splice(order.indexOf('intelligence'), 0, 'cost');
    component.onTableColumnsChange(columnsWith({ order, show: ['intervalHalfWidth'], hide: ['notes'] }));
    refresh();

    expect(tableHeaders()).toEqual([
      'Model', 'R', 'State', 'Candidate $ / question', 'Intelligence Index', 'Speed Index', 'Timings',
      'Total $ / run, with grading', '±'
    ]);
    expect(JSON.parse(localStorage.getItem(TABLE_COLUMNS_STORAGE_KEY)!)).toEqual({
      version: 2, order, shown: component.tableColumns.shown
    });
    // The ± is its own column now, so the Intelligence Index cell stops printing it.
    const row = tableRowOf('Run 1');
    const intelligenceCell = row.children[4];
    expect(intelligenceCell.querySelector('.mc-nowrap')?.textContent?.trim()).toMatch(/^\d+\.\d$/);
    expect(intelligenceCell.querySelector('.mc-interval')).toBeNull();
    expect(row.lastElementChild?.textContent?.trim()).toBe('6.4');
    expect(fixture.debugElement.query(By.css('.mc-notes'))).toBeNull();

    // The Table tab edits the same configuration.
    openSidebarTab('table');
    const panel = fixture.debugElement.query(By.css('#mc-side-panel-table app-table-settings-panel'));
    expect(panel).toBeTruthy();
    expect((panel.componentInstance as { columns: unknown }).columns).toBe(component.tableColumns);
  });

  it('clears the filter of a column it hides, with a status line, and returns a sort on it to the model order', () => {
    const entries = comparableSet(3);
    entries[0] = { ...entries[0], state: 'Degraded', speedDegraded: true, speedDegradingKeys: ['ParallelMode'] };
    renderTable(buildDto(entries));
    component.entryTable.setFilter('state', 'Degraded');
    component.onTableChanged();
    headerButton('State').click();
    refresh();
    expect(component.entryTable.filteredCount(component.entries)).toBe(1);

    component.onTableColumnsChange(columnsWith({ hide: ['stateCol'] }));
    refresh();

    expect(component.entryTable.filters['state'] ?? '').toBe('');
    expect(component.entryTable.filteredCount(component.entries)).toBe(3);
    expect(component.entryTable.sortColumn).toBe('modelOrder');
    expect(component.tableColumnsStatus).toBe('The State filter was cleared because its column is hidden.');
    expect(fixture.debugElement.query(By.css('#mc-f-state'))).toBeNull();
    openSidebarTab('table');
    expect(textOf('.mc-columns-status')).toContain('The State filter was cleared because its column is hidden.');
  });

  it('leaves a part out of its combined cell while that part is shown as a column of its own', () => {
    const entries = comparableSet(2);
    entries[0] = { ...entries[0], table: { ...entries[0].table!, speedIndexSaturated: true } };
    renderTable(buildDto(entries));
    const combined = tableRowOf('Run 1');
    expect(combined.querySelector('.thinking-badge')).toBeTruthy();
    expect(combined.querySelector('.provider-badge')).toBeTruthy();
    expect(combined.querySelector('.mc-saturated')).toBeTruthy();

    component.onTableColumnsChange(columnsWith({
      show: ['thinkingLevel', 'provider', 'source', 'explanation', 'speedIndexSaturated', 'intervalBasis']
    }));
    refresh();

    const row = fixture.debugElement.queryAll(By.css('table.mc-table tbody tr'))
      .map(candidate => candidate.nativeElement as HTMLElement)
      .find(candidate => candidate.textContent?.includes('run:1') || candidate.textContent?.includes('Run 1'))!;
    expect(row).toBeTruthy();
    expect(row.querySelector('.thinking-badge')).toBeNull();
    expect(row.querySelector('.provider-badge')).toBeNull();
    expect(row.querySelector('.mc-source')).toBeNull();
    expect(row.querySelector('.mc-state')?.getAttribute('interestfor')).toBeNull();
    expect(row.querySelector('.mc-saturated')).toBeNull();
    expect(row.querySelector('.mc-basis-btn')).toBeNull();
    // The reasoning badge has no column of its own, so it never leaves.
    expect(tableHeaders()).toContain('Thinking level');
    expect(tableHeaders()).toContain('Explanation');
  });

  it('restores the column configuration from storage and repairs it', () => {
    localStorage.setItem(TABLE_COLUMNS_STORAGE_KEY, JSON.stringify({
      version: 2, order: ['cost', 'bogus', 'model', 'cost'], shown: ['cost', 'bogus']
    }));
    let stored = TestBed.createComponent(ModelComparisonComponent);
    const columns = stored.componentInstance.tableColumns;
    expect(columns.order.slice(0, 2)).toEqual(['cost', 'model']);
    expect(columns.order).not.toContain('bogus');
    expect(columns.order.length).toBe(DEFAULT_TABLE_COLUMNS.order.length);
    // Model is always shown, whatever was stored.
    expect(columns.shown).toEqual(['cost', 'model']);
    expect(stored.componentInstance.shownColumns.map(column => column.key)).toEqual(['cost', 'model']);
    stored.destroy();

    localStorage.setItem(TABLE_COLUMNS_STORAGE_KEY, '{not json');
    stored = TestBed.createComponent(ModelComparisonComponent);
    expect(stored.componentInstance.tableColumns).toEqual(DEFAULT_TABLE_COLUMNS);
    stored.destroy();
  });

  it('shows the total-cost column on load to an admin whose version-1 layout predates it', () => {
    localStorage.setItem(TABLE_COLUMNS_STORAGE_KEY, JSON.stringify({
      version: 1,
      order: DEFAULT_TABLE_COLUMNS.order.filter(key => key !== 'totalCost'),
      shown: DEFAULT_TABLE_COLUMNS.shown.filter(key => key !== 'totalCost')
    }));
    fixture.destroy();
    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;

    renderTable(buildDto(comparableSet(2)));

    const headers = tableHeaders();
    expect(headers[headers.indexOf('Candidate $ / question') + 1]).toBe('Total $ / run, with grading');
  });

  it('renders the run total with its SD in the total-cost cell', () => {
    renderTable(buildDto(comparableSet(2)));

    const cell = tableRowOf('Run 1').children[tableHeaders().indexOf('Total $ / run, with grading')] as HTMLElement;
    expect(cell.querySelector('.mc-nowrap')?.textContent).toContain('$0.9876');
    expect(cell.querySelector('.mc-interval')?.textContent).toContain('± $0.0432');
    expect(cell.querySelector('.mc-badge')).toBeNull();
  });

  it('marks the total-cost cell Unavailable, with the reason in its tooltip, when the total is null', () => {
    renderTable(buildDto(setWithoutTotal(2, PRE_HARNESS_15)));

    const cell = tableRowOf('Run 2').children[tableHeaders().indexOf('Total $ / run, with grading')] as HTMLElement;
    expect(cell.querySelector('.mc-nowrap')?.textContent?.trim()).toBe('—');
    expect(cell.querySelector('.mc-interval')).toBeNull();
    const badge = cell.querySelector('.mc-badge') as HTMLElement;
    expect(badge.textContent?.trim()).toBe('Unavailable');
    const tip = cell.querySelector(`#${badge.getAttribute('interestfor')}`) as HTMLElement;
    expect(tip.textContent).toContain('harness 15');
  });

  it('computes the Columns empty set from the rows passing the filters, not in the template', () => {
    const entries = comparableSet(2);
    entries[1] = { ...entries[1], cost: { ...entries[1].cost!, scheduledChangeEffectiveFrom: '2026-10-01' } };
    renderTable(buildDto(entries));
    expect(component.columnEmptyKeys.has('scheduledChange')).toBe(false);
    expect(component.columnEmptyKeys.has('differsOn')).toBe(true);

    // Model 1 alone has no scheduled change.
    component.entryTable.setFilter('label', 'Model 1');
    component.onTableChanged();
    expect(component.columnEmptyKeys.has('scheduledChange')).toBe(true);

    openSidebarTab('table');
    const panel = fixture.debugElement.query(By.css('app-table-settings-panel'));
    expect((panel.componentInstance as { empty: unknown }).empty).toBe(component.columnEmptyKeys);
  });

  // -------------------------------------------------------------------------------------------
  // The sidebar's tab sets, and the Table preview
  // -------------------------------------------------------------------------------------------

  it('shows Table in place of Charts beside the table, keeps a shared tab across a switch, and swaps Charts and Table', () => {
    render(buildDto(comparableSet(3)), 2);
    const labels = (): string[] => fixture.debugElement.queryAll(By.css('.mc-fig-sidebar-tabs [role="tab"]'))
      .map(tab => (tab.nativeElement as HTMLElement).textContent?.trim() ?? '');
    expect(labels()).toEqual(['Data', 'Theme', 'Charts', 'Download']);

    openSidebarTab('charts');
    showView('table');
    expect(labels()).toEqual(['Data', 'Theme', 'Table', 'Download']);
    expect(component.effectiveSidebarTab).toBe('table');
    expect(fixture.debugElement.query(By.css('#mc-side-panel-table'))).toBeTruthy();

    // Within one group the tab never changes.
    showView('tablePreview');
    expect(component.effectiveSidebarTab).toBe('table');
    showView('all');
    expect(component.effectiveSidebarTab).toBe('charts');
    showView('single');
    expect(component.effectiveSidebarTab).toBe('charts');

    for (const shared of ['data', 'theme', 'download'] as FigureSidebarTab[]) {
      openSidebarTab(shared);
      showView('table');
      expect(component.effectiveSidebarTab, shared).toBe(shared);
      showView('all');
      expect(component.effectiveSidebarTab, shared).toBe(shared);
    }
  });

  it('runs the roving tabindex over the table views\' tabs', () => {
    renderTable(buildDto(comparableSet(3)));

    expectTabContract('.mc-fig-sidebar-tabs', 'Settings sections', 'mc-side-tab-', 'mc-side-panel-',
      ['Data', 'Theme', 'Table', 'Download'], () => component.effectiveSidebarTab);
  });

  it('migrates stored sidebar tabs: Emphasis to Data, Style to Charts, Export to Download', () => {
    const storedTab = (tab: string): string => {
      localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, tab, view: 'all' }));
      const stored = TestBed.createComponent(ModelComparisonComponent);
      const result = stored.componentInstance.sidebarTab;
      stored.destroy();
      return result;
    };
    expect(storedTab('emphasis')).toBe('data');
    expect(storedTab('style')).toBe('charts');
    expect(storedTab('export')).toBe('download');
    expect(storedTab('download')).toBe('download');
    expect(storedTab('theme')).toBe('theme');
    expect(storedTab('gallery')).toBe('data');
    // A stored Table opens as Charts beside the charts, and Charts as Table beside the table.
    expect(sidebarTabForView('table', 'all')).toBe('charts');
    expect(sidebarTabForView('charts', 'tablePreview')).toBe('table');
    expect(sidebarTabForView('download', 'table')).toBe('download');

    localStorage.setItem(FIGURE_SIDEBAR_STORAGE_KEY, JSON.stringify({ version: 1, collapsed: false, tab: 'table', view: 'all' }));
    fixture = TestBed.createComponent(ModelComparisonComponent);
    component = fixture.componentInstance;
    render(buildDto(comparableSet(3)), 2);
    expect(component.effectiveSidebarTab).toBe('charts');
    expect(fixture.debugElement.query(By.css('#mc-side-panel-charts'))).toBeTruthy();
  });

  it('shows the table image on the Table preview stage, with its own view state and its pixel size', async () => {
    render(buildDto(comparableSet(3)), 2);
    openSingle(component.panelCards[0]);
    component.setPreviewView(2);
    expect(component.previewView).toBe(2);

    showView('tablePreview');
    expect(component.previewView, 'it opens at Fit to screen').toBe('fitScreen');
    expect(textOf('#mc-fig-panel-tablePreview .mc-preview-label')).toBe('Comparison table');
    expect(fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview .mc-preview-export'))).toBeNull();
    // The row is label, zoom, then the table's own export group at its end.
    const row = fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview .mc-preview-toolbar')).nativeElement as HTMLElement;
    expect(Array.from(row.children).filter(child => !child.hasAttribute('popover'))
      .map(child => child.classList.contains('mc-preview-label') ? 'label'
      : child.classList.contains('mc-preview-zoom') ? 'zoom'
        : child.classList.contains('mc-table-export') ? 'export' : child.tagName))
      .toEqual(['label', 'zoom', 'export']);
    expect(fixture.debugElement.query(By.css('#mc-preview-figure'))).toBeNull();
    // The test page lays the stage out at no size, so the fit is given one, as the Single chart specs do.
    vi.spyOn(component, 'measureStage').mockReturnValue({ width: 800, height: 600, devicePixelRatio: 2 });
    await composePreview();

    expect(component.tablePreviewPixels).toMatch(/^\d+ × \d+ px$/);
    expect(textOf('.mc-table-preview-size')).toBe(component.tablePreviewPixels);
    const canvas = fixture.debugElement.query(By.css('#mc-fig-panel-tablePreview canvas.mc-preview-canvas'))
      .nativeElement as HTMLCanvasElement;
    expect(canvas.getAttribute('role')).toBe('img');
    expect(canvas.getAttribute('aria-label')).toBe('Table preview: 3 entries, 9 columns');
    expect(canvas.width).toBeGreaterThan(0);
    expect(textOf('.mc-table-preview-notes')).toContain(
      'Previewing the image export. The chosen format, Excel (.xlsx), has no appearance of its own.');

    chooseTableFormat('image');
    expect(textOf('.mc-table-preview-notes')).not.toContain('Previewing the image export');

    // Back to Single: its own zoom, and the table's bitmap released.
    showView('single');
    expect(component.previewView).toBe(2);
    expect(component.tablePreviewPixels).toBe('');
  });

  it('shows a refused table image size under the Table preview stage instead of the image', async () => {
    render(buildDto(comparableSet(3)), 2);
    showView('tablePreview');
    component.onTableImageSizeChange({ ...component.tableImageSize, resolutionId: 'custom', customWidthPx: 400, customHeightPx: 320 });
    await composePreview();

    expect(component.previewRefusal).toMatch(/need at least \d+ px/);
    expect(textOf('.mc-table-preview-notes [role="alert"]')).toBe(component.previewRefusal);
    expect(component.tablePreviewPixels).toBe('');
  });

  // -------------------------------------------------------------------------------------------
  // The Theme tab
  // -------------------------------------------------------------------------------------------

  it('hosts the appearance panel in the Theme tab of both view groups', () => {
    render(buildDto(comparableSet(3)), 2);
    openSidebarTab('theme');
    const panel = (): { kind: string; fontLoadStatus: string; figureStyle: FigureStyle } =>
      fixture.debugElement.query(By.css('#mc-side-panel-theme app-figure-style-panel')).componentInstance;
    expect(panel().kind).toBe('appearance');
    expect(panel().figureStyle).toBe(component.figureStyle);

    showView('table');
    expect(panel().kind).toBe('appearance');
  });

  it('resolves the theme once and hands it to every chart\'s chrome, and paints a transparent background\'s backdrop on screen only', () => {
    render(buildDto(comparableSet(3)), 2);
    expect(component.figureTheme.name).toBe('dark');
    expect(fixture.debugElement.queryAll(By.css('.mc-all-canvas.is-transparent-figure')).length).toBe(0);

    withStyleDebounce(() => component.onFigureStyleChange({
      ...component.figureStyle,
      appearance: {
        ...component.figureStyle.appearance,
        theme: 'light', background: 'transparent', previewBackdrop: 'color', previewBackdropColor: '#123456'
      }
    }));

    expect(component.figureTheme.name).toBe('light');
    expect(component.figureTheme.background).toBeNull();
    const chrome = (component as unknown as { exportChrome(card: ComparisonFigureCard): { theme?: unknown } })
      .exportChrome(component.panelCards[0]);
    expect(chrome.theme).toBe(component.figureTheme);
    expect(fixture.debugElement.queryAll(By.css('.mc-all-canvas.is-transparent-figure')).length).toBe(7);
    const viewport = fixture.debugElement.query(By.css('.mc-all-viewport')).nativeElement as HTMLElement;
    expect(viewport.getAttribute('style') ?? '').toContain('--gh-fig-backdrop: #123456');
  });

  it('copies one figure to the clipboard and names the card in the status', async () => {
    render(buildDto(comparableSet(3)), 2);
    const write = vi.fn().mockName('write').mockResolvedValue(undefined);
    withClipboard({ write });
    const card = component.panelCards[0];

    await component.copyFigure(card);

    expect(write).toHaveBeenCalledTimes(1);
    expect(component.exportStatus).toBe(`Copied ${card.title} to the clipboard.`);
    expect(component.exporting).toBe(false);
  });

  it('reports a refused figure copy, and an absent clipboard API, as inline text', async () => {
    render(buildDto(comparableSet(3)), 2);
    withClipboard({ write: () => Promise.reject(new Error('Write permission denied.')) });

    await component.copyFigure(component.panelCards[0]);
    expect(component.exportStatus).toBe('The clipboard write was refused.');

    withClipboard(undefined);
    await component.copyFigure(component.panelCards[0]);
    expect(component.exportStatus).toContain('cannot copy images to the clipboard');
    expect(component.exportStatus).toContain('download the figure instead');
    expect(component.exporting).toBe(false);
  });
});
