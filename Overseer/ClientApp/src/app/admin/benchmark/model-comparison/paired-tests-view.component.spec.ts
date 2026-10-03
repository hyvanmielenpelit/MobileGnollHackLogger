import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkPairedComparisonDto,
  BenchmarkPairedComparisonMode,
  BenchmarkPairedMeasureDto,
  BenchmarkPairedTestDto,
  BenchmarkReportPackPricingBasis
} from '../../../services/admin-benchmark.service';
import { BenchmarkModelComparisonDto, BenchmarkModelComparisonEntryDto } from './model-comparison.models';
import { PAIRED_VIEW_SECTIONS_STORAGE_KEY, PairedTestsViewComponent } from './paired-tests-view.component';

// The Model Comparison wizard's Paired tests view, against a stubbed service.

function entry(index: number, overrides: Partial<BenchmarkModelComparisonEntryDto> = {}): BenchmarkModelComparisonEntryDto {
  return {
    key: `run:${index}`,
    sourceKind: 'Run',
    sourceId: index,
    runIds: [index],
    runCount: 1,
    provider: 'Google',
    modelId: `model-${index}`,
    modelDisplayName: `Model ${index}`,
    thinkingLevel: index === 2 ? 'high' : null,
    reasoningMode: null,
    parallelExecutionMode: 'Enabled',
    label: `Model ${index}`,
    firstRunStartedAtUtc: '2026-09-01T10:00:00Z',
    lastRunStartedAtUtc: '2026-09-01T10:00:00Z',
    state: 'Comparable',
    comparable: true,
    excluded: false,
    speedDegraded: false,
    costDegraded: false,
    excludingKeys: [],
    speedDegradingKeys: [],
    costDegradingKeys: [],
    differences: [],
    explanation: '',
    ...overrides
  } as BenchmarkModelComparisonEntryDto;
}

function comparison(count: number, extra: BenchmarkModelComparisonEntryDto[] = []): BenchmarkModelComparisonDto {
  return {
    pricingBasis: 'Current',
    pricingBasisLabel: 'Current catalog',
    computedAtUtc: '2026-10-03T12:00:00Z',
    baselineEntryKeys: [],
    baselineKeyValues: {},
    baselineSignature: 'sig',
    modelAxisKeys: ['ModelId'],
    entries: [...Array.from({ length: count }, (_unused, index) => entry(index + 1)), ...extra],
    comparableCount: count,
    excludedCount: extra.length,
    thinkingLevelsDiffer: false,
    explanation: '',
    excludedMeasures: []
  };
}

function pairTest(baselineKey: string, treatmentKey: string, kind: 'Difference' | 'Ratio', overrides: Partial<BenchmarkPairedTestDto> = {}): BenchmarkPairedTestDto {
  const ratio = kind === 'Ratio';
  return {
    baselineKey,
    treatmentKey,
    pairedItems: 18,
    unpairedItems: 0,
    revisionMismatched: 0,
    effect: ratio ? 0.82 : 3.2,
    effectLower: ratio ? 0.71 : -0.8,
    effectUpper: ratio ? 0.95 : 7.1,
    effectKind: kind,
    dz: ratio ? null : 0.45,
    pValue: 0.012,
    adjustedPValue: 0.036,
    method: 'Wilcoxon signed-rank',
    direction: ratio ? 'Lower' : 'Higher',
    established: true,
    verdict: ratio ? 'Faster on the same questions' : 'Higher on the same questions',
    notTestedReason: null,
    note: null,
    suites: null,
    ...overrides
  };
}

/** The pairs the server plans: the reference against every other entry, or i against j for i < j. */
function plannedPairs(keys: string[], mode: BenchmarkPairedComparisonMode, reference: string): [string, string][] {
  if (keys.length === 2 || mode === 'Reference') {
    return keys.filter(key => key !== reference).map(key => [reference, key]);
  }
  const pairs: [string, string][] = [];
  for (let i = 0; i < keys.length; i++) {
    for (let j = i + 1; j < keys.length; j++) {
      pairs.push([keys[i], keys[j]]);
    }
  }
  return pairs;
}

function pairedDto(
  keys: string[],
  mode: BenchmarkPairedComparisonMode,
  reference: string,
  speedNotTested: string | null = null
): BenchmarkPairedComparisonDto {
  const pairs = plannedPairs(keys, mode, reference);
  const adjusted = pairs.length > 1;
  const family = (
    measure: string, label: string, category: BenchmarkPairedMeasureDto['category'], kind: 'Difference' | 'Ratio',
    notTested: string | null = null
  ): BenchmarkPairedMeasureDto => ({
    measure,
    label,
    category,
    primary: category === 'Intelligence',
    familySize: notTested ? 0 : pairs.length,
    adjustment: adjusted ? 'Holm' : 'None',
    adjustmentNote: adjusted ? `Holm-adjusted across ${pairs.length} tests` : 'Single comparison — no adjustment needed',
    notTestedReason: notTested,
    caption: kind === 'Ratio' ? 'Tested as Wilcoxon signed-rank on log(B / A).' : null,
    pairs: pairs.map(([baseline, treatment], index) => pairTest(baseline, treatment, kind, notTested
      ? { notTestedReason: notTested, verdict: 'Not tested', established: false, pValue: null, adjustedPValue: null }
      : { effect: kind === 'Ratio' ? 0.8 + index / 10 : index - 1, established: index % 2 === 0,
        verdict: index % 2 === 0 ? (kind === 'Ratio' ? 'Faster on the same questions' : 'Higher on the same questions') : 'No difference established' }))
  });
  return {
    computedAtUtc: '2026-10-03T12:00:01Z',
    subjectKind: 'Runs',
    pricingBasis: 'Current',
    mode: keys.length === 2 ? 'Reference' : mode,
    referenceKey: keys.length === 2 || mode === 'Reference' ? reference : null,
    entryKeys: keys,
    allPairsLimit: 12,
    singleRunCaveat: 'At least one entry has a single run. With one run a side, a paired test captures question sampling only.',
    measuresNote: 'Each measure is its own family of tests. The measures are separate questions and are not adjusted for each other.',
    measures: [
      family('Intelligence', 'Intelligence Index', 'Intelligence', 'Difference'),
      family('Accuracy', 'Accuracy', 'QualityDimension', 'Difference'),
      family('Speed', 'Speed', 'Speed', 'Ratio', speedNotTested),
      family('Cost', 'Cost', 'Cost', 'Ratio')
    ]
  };
}

describe('PairedTestsViewComponent', () => {
  let fixture: ComponentFixture<PairedTestsViewComponent>;
  let service: MockedObject<AdminBenchmarkService>;

  beforeEach(async () => {
    localStorage.removeItem(PAIRED_VIEW_SECTIONS_STORAGE_KEY);
    service = {
      getPairedComparison: vi.fn().mockName('AdminBenchmarkService.getPairedComparison')
    } as unknown as MockedObject<AdminBenchmarkService>;
    await TestBed.configureTestingModule({
      imports: [PairedTestsViewComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
    fixture = TestBed.createComponent(PairedTestsViewComponent);
  });

  afterEach(() => {
    localStorage.removeItem(PAIRED_VIEW_SECTIONS_STORAGE_KEY);
    delete (navigator as unknown as { clipboard?: unknown }).clipboard;
    vi.restoreAllMocks();
  });

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string): string {
    return (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function show(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  function click(selector: string): void {
    const target = el().querySelector(selector) as HTMLElement | null;
    expect(target, selector).not.toBeNull();
    target!.click();
    fixture.detectChanges();
  }

  function lastRequest(): Parameters<AdminBenchmarkService['getPairedComparison']>[0] {
    return service.getPairedComparison.mock.calls.at(-1)![0];
  }

  it('fetches nothing until it is shown', () => {
    service.getPairedComparison.mockReturnValue(of(pairedDto(['run:1', 'run:2'], 'Reference', 'run:2')));
    show({ comparison: comparison(2), active: false });
    expect(service.getPairedComparison).not.toHaveBeenCalled();

    show({ active: true });
    expect(service.getPairedComparison).toHaveBeenCalledTimes(1);
  });

  it('makes one unadjusted test of two entries, with no Mode control', () => {
    service.getPairedComparison.mockReturnValue(of(pairedDto(['run:1', 'run:2'], 'Reference', 'run:2')));
    show({ comparison: comparison(2), active: true });

    expect(lastRequest()).toEqual({
      runIds: [1, 2], groupIds: [], batteryRunIds: [], pricingBasis: BenchmarkReportPackPricingBasis.Current,
      mode: 'Reference', referenceKey: null
    });
    expect(el().querySelector('.mcp-mode')).toBeNull();
    expect(text('.mcp-family')).toBe('1 test: Model 1 against Model 2 · Single comparison — no adjustment needed');
    expect(text('#mc-paired-intelligence .mcp-adjustment')).toBe('Single comparison — no adjustment needed');
    expect(text('.mcp-caveat')).toContain('single run');
    expect(text('.mcp-measures-note')).toContain('not adjusted for each other');
    expect(el().querySelectorAll('#mc-paired-intelligence tbody tr').length).toBe(1);
    // The reference is the baseline: the other entry is tested against it.
    expect(text('#mc-paired-intelligence tbody tr .mcp-model-btn')).toContain('Model 1');
    expect(text('#mc-paired-intelligence tbody tr td')).toBe('Model 2');
    // The reference select shows the entry the server chose.
    expect((el().querySelector('#mc-paired-reference') as HTMLSelectElement).value).toBe('run:2');
    // Primary first, then the dimensions, speed and cost.
    expect(Array.from(el().querySelectorAll('details.mcp-section')).map(section => section.id)).toEqual([
      'mc-paired-section-intelligence', 'mc-paired-section-dimensions', 'mc-paired-section-speed', 'mc-paired-section-cost'
    ]);
    expect(text('#mc-paired-section-intelligence summary')).toContain('primary test');
  });

  it('tests every entry against the first highlighted one, Holm-adjusted, and refetches when the reference changes', () => {
    const keys = ['run:1', 'run:2', 'run:3', 'run:4'];
    const first = new Subject<BenchmarkPairedComparisonDto>();
    service.getPairedComparison.mockReturnValueOnce(first);
    service.getPairedComparison.mockReturnValueOnce(of(pairedDto(keys, 'Reference', 'run:3')));
    show({ comparison: comparison(4), highlightedKeys: ['run:3'], active: true });

    expect(lastRequest().referenceKey).toBe('run:3');
    expect(lastRequest().mode).toBe('Reference');
    expect(text('.mcp-status')).toBe('Computing paired tests…');

    // A new reference before the first answer: the first request is dropped, never rendered.
    const select = el().querySelector('#mc-paired-reference') as HTMLSelectElement;
    select.value = 'run:3';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(service.getPairedComparison).toHaveBeenCalledTimes(1);

    service.getPairedComparison.mockReset();
    service.getPairedComparison.mockReturnValue(of(pairedDto(keys, 'Reference', 'run:1')));
    select.value = 'run:1';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(lastRequest().referenceKey).toBe('run:1');
    first.next(pairedDto(keys, 'Reference', 'run:3'));
    fixture.detectChanges();

    expect(text('.mcp-family')).toBe('3 tests against Model 1 · Holm-adjusted');
    const rows = Array.from(el().querySelectorAll('#mc-paired-intelligence tbody tr'));
    expect(rows.length).toBe(3);
    expect(rows.map(row => row.querySelector('td')!.textContent!.trim())).toEqual(['Model 1', 'Model 1', 'Model 1']);
    expect(text('#mc-paired-intelligence thead')).toContain('Adjusted p');
    expect(text('#mc-paired-intelligence .mcp-adjustment')).toBe('Holm-adjusted across 3 tests');
    expect(text('#mc-paired-intelligence tbody tr:first-child .mcp-adjusted-p')).toBe('0.04');
    // A badge rides with the model name.
    expect(text('#mc-paired-intelligence tbody tr:first-child .thinking-badge')).toBe('high');
    // The forest strip is decorative: one per row, hidden from assistive technology.
    expect(el().querySelectorAll('#mc-paired-intelligence td.mcp-forest[aria-hidden="true"] svg').length).toBe(3);
    // An established verdict is a filled dot, any other a ring; the word is always there.
    const verdicts = Array.from(el().querySelectorAll('#mc-paired-intelligence .mcp-verdict-cell'));
    expect(verdicts.map(cell => cell.textContent!.trim()))
      .toEqual(['Higher on the same questions', 'No difference established', 'Higher on the same questions']);
    expect(verdicts.map(cell => cell.querySelector('circle')!.getAttribute('fill'))).toEqual(['currentColor', 'none', 'currentColor']);
  });

  it('opens a row\'s details below the sections', () => {
    const keys = ['run:1', 'run:2', 'run:3'];
    service.getPairedComparison.mockReturnValue(of(pairedDto(keys, 'Reference', 'run:1')));
    show({ comparison: comparison(3), active: true });

    click('#mc-paired-intelligence tbody tr:nth-child(2) .mcp-model-btn');
    expect(text('#mc-paired-detail-title')).toBe('Model 3 against Model 1');
    expect(document.activeElement).toBe(el().querySelector('#mc-paired-detail-title'));
    expect(text('#mc-paired-detail .ptr-adjusted-p')).toBe('0.04');
    expect((el().querySelector('#mc-paired-intelligence tbody tr:nth-child(2) .mcp-model-btn') as HTMLElement)
      .getAttribute('aria-expanded')).toBe('true');

    click('#mc-paired-detail .gh-filter-clear');
    expect(el().querySelector('#mc-paired-detail')).toBeNull();
  });

  it('shows All pairs as a lower-triangle matrix whose cells open the pair\'s details', () => {
    const keys = ['run:1', 'run:2', 'run:3', 'run:4'];
    service.getPairedComparison.mockReturnValue(of(pairedDto(keys, 'Reference', 'run:4')));
    show({ comparison: comparison(4), active: true });

    service.getPairedComparison.mockReturnValue(of(pairedDto(keys, 'AllPairs', 'run:4')));
    click('#mc-paired-mode-AllPairs');
    expect(lastRequest().mode).toBe('AllPairs');
    expect(lastRequest().referenceKey).toBeNull();
    expect(el().querySelector('#mc-paired-reference')).toBeNull();
    expect(text('.mcp-family')).toBe('6 tests, every pair of 4 models · Holm-adjusted');
    expect(el().querySelector('#mc-paired-mode-AllPairs')!.getAttribute('aria-selected')).toBe('true');

    const matrix = el().querySelector('#mc-paired-intelligence table.mc-paired-matrix')!;
    expect(Array.from(matrix.querySelectorAll('thead th')).map(th => th.textContent!.trim())).toEqual(['Model 1', 'Model 2', 'Model 3']);
    expect(matrix.querySelectorAll('tbody tr').length).toBe(3);
    expect(matrix.querySelectorAll('tbody .mc-paired-cell button').length).toBe(6);
    expect(matrix.querySelectorAll('tbody .mc-paired-cell-empty').length).toBe(3);
    // Row 3 (Model 4) against column 2 (Model 2).
    const cell = matrix.querySelector('tbody tr:nth-child(3) td:nth-child(3) button') as HTMLButtonElement;
    expect(cell.getAttribute('aria-label')).toContain('Model 4 against Model 2, Intelligence Index');

    cell.click();
    fixture.detectChanges();
    expect(text('#mc-paired-detail-title')).toBe('Model 4 against Model 2');
    expect(cell.getAttribute('aria-expanded')).toBe('true');
    // The list repeats the matrix in rows, for screen readers and for export.
    expect(el().querySelectorAll('#mc-paired-intelligence .mcp-pair-list li').length).toBe(6);
  });

  it('refuses All pairs above twelve comparable entries and says why', () => {
    const keys = Array.from({ length: 13 }, (_unused, index) => `run:${index + 1}`);
    service.getPairedComparison.mockReturnValue(of(pairedDto(keys, 'Reference', 'run:13')));
    show({ comparison: comparison(13, [entry(20, { excluded: true, state: 'Excluded' })]), active: true });

    const allPairs = el().querySelector('#mc-paired-mode-AllPairs') as HTMLButtonElement;
    expect(allPairs.getAttribute('aria-disabled')).toBe('true');
    expect(allPairs.getAttribute('aria-describedby')).toBe('mc-paired-allpairs-reason');
    expect(text('#mc-paired-allpairs-reason')).toContain('at most 12 comparable models; this comparison has 13');

    allPairs.click();
    fixture.detectChanges();
    expect(service.getPairedComparison).toHaveBeenCalledTimes(1);
    expect(lastRequest().mode).toBe('Reference');
    // The excluded entry takes no part and is not offered as the reference.
    expect(lastRequest().runIds).toContain(20);
    expect(Array.from((el().querySelector('#mc-paired-reference') as HTMLSelectElement).options).map(o => o.value))
      .not.toContain('run:20');
  });

  it('says a degraded speed axis was not tested, and why', () => {
    const reason = 'Not tested: the speed axis is degraded because ParallelExecutionMode differs.';
    service.getPairedComparison.mockReturnValue(of(pairedDto(['run:1', 'run:2', 'run:3'], 'Reference', 'run:1', reason)));
    show({ comparison: comparison(3), active: true });

    expect(text('#mc-paired-speed .mcp-not-tested')).toContain('ParallelExecutionMode differs');
    const rows = Array.from(el().querySelectorAll('#mc-paired-speed tbody tr'));
    expect(rows.length).toBe(2);
    expect(rows.every(row => row.textContent!.includes('ParallelExecutionMode differs'))).toBe(true);
    expect(rows.every(row => row.querySelector('.mcp-verdict-cell line') !== null)).toBe(true);
    expect(text('#mc-paired-cost tbody')).toContain('0.80×');
  });

  it('shows a refusal and keeps Recompute able to retry', () => {
    service.getPairedComparison.mockReturnValue(throwError(() => ({ status: 400, error: 'Comparing needs two comparable entries.' })));
    show({ comparison: comparison(2), active: true });
    expect(text('.mcp-error')).toBe('Comparing needs two comparable entries.');

    service.getPairedComparison.mockReturnValue(of(pairedDto(['run:1', 'run:2'], 'Reference', 'run:2')));
    click('.mcp-recompute');
    expect(lastRequest().recompute).toBe(true);
    expect(el().querySelector('.mcp-error')).toBeNull();
    expect(text('.mcp-family')).toContain('Single comparison');
  });

  it('refetches when the pricing basis changes, and not for an unchanged request', () => {
    service.getPairedComparison.mockReturnValue(of(pairedDto(['run:1', 'run:2'], 'Reference', 'run:2')));
    show({ comparison: comparison(2), active: true });
    show({ highlightedKeys: [] });
    expect(service.getPairedComparison).toHaveBeenCalledTimes(1);

    show({ pricingBasis: 'AsRun' });
    expect(service.getPairedComparison).toHaveBeenCalledTimes(2);
    expect(lastRequest().pricingBasis).toBe(BenchmarkReportPackPricingBasis.AsRun);
  });

  it('copies and downloads the same rows, as Markdown or as CSV per the table format', async () => {
    const keys = ['run:1', 'run:2', 'run:3'];
    service.getPairedComparison.mockReturnValue(of(pairedDto(keys, 'Reference', 'run:1')));
    show({ comparison: comparison(3), active: true, tableFormat: 'md' });

    let copied = '';
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: (value: string) => { copied = value; return Promise.resolve(); } },
      configurable: true
    });
    const blobs: Blob[] = [];
    const names: string[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((source: Blob | MediaSource) => {
      blobs.push(source as Blob);
      return 'blob:paired-tests';
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      names.push(this.download);
    });

    await fixture.componentInstance.copyMarkdown();
    fixture.detectChanges();
    expect(text('.mcp-status')).toBe('Copied the paired tests as Markdown.');
    expect(el().querySelector('.mcp-download')!.getAttribute('aria-label')).toBe('Download the paired tests as Markdown');

    click('.mcp-download');
    expect(names[0]).toMatch(/^paired-tests_\d{8}-\d{6}\.md$/);
    const markdown = await blobs[0].text();
    expect(markdown).toBe(copied);

    const markdownRows = markdown.split('\n').filter(line => line.startsWith('| ') && !line.startsWith('| Model |') && !line.startsWith('| ---'));
    // Four measures, two pairs each.
    expect(markdownRows.length).toBe(8);

    show({ tableFormat: 'csv' });
    expect(el().querySelector('.mcp-download')!.getAttribute('aria-label')).toBe('Download the paired tests as CSV');
    click('.mcp-download');
    expect(names[1]).toMatch(/\.csv$/);
    const csv = (await blobs[1].text()).replace(/^﻿/, '');
    const csvRows = csv.split('\r\n').filter(line => line !== '').slice(1);
    expect(csvRows.length).toBe(markdownRows.length);
    expect(csvRows.map(row => row.split(',')[2])).toEqual(markdownRows.map(row => row.split(' | ')[0].replace('| ', '')));
  });

  it('remembers which measure sections are open, per browser', () => {
    service.getPairedComparison.mockReturnValue(of(pairedDto(['run:1', 'run:2'], 'Reference', 'run:2')));
    show({ comparison: comparison(2), active: true });
    const cost = el().querySelector('#mc-paired-section-cost') as HTMLDetailsElement;
    expect(cost.open).toBe(true);
    cost.open = false;
    cost.dispatchEvent(new Event('toggle'));
    expect(JSON.parse(localStorage.getItem(PAIRED_VIEW_SECTIONS_STORAGE_KEY)!).open.cost).toBe(false);
  });

  it('explains that one comparable entry cannot be compared, and fetches nothing', () => {
    show({ comparison: comparison(1, [entry(5, { excluded: true, state: 'Excluded' })]), active: true });
    expect(service.getPairedComparison).not.toHaveBeenCalled();
    expect(text('.mcp-needs-two')).toContain('needs a second comparable entry');
    expect(text('.mcp-needs-two')).toContain('run report');
  });

  it('stops the request in flight when the view is destroyed', () => {
    const pending = new Subject<BenchmarkPairedComparisonDto>();
    service.getPairedComparison.mockReturnValue(pending as Observable<BenchmarkPairedComparisonDto>);
    show({ comparison: comparison(2), active: true });
    expect(pending.observed).toBe(true);
    fixture.destroy();
    expect(pending.observed).toBe(false);
  });
});
