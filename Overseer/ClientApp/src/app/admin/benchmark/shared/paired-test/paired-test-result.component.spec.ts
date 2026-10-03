import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  BenchmarkPairComparisonDto,
  BenchmarkPairedMeasureDto,
  BenchmarkPairedTestDto
} from '../../../../services/admin-benchmark.service';
import { PAIRED_RESULT_SECTIONS_STORAGE_KEY, PairedTestResultComponent } from './paired-test-result.component';

function pair(overrides: Partial<BenchmarkPairedTestDto> = {}): BenchmarkPairedTestDto {
  return {
    baselineKey: 'run:1',
    treatmentKey: 'run:2',
    pairedItems: 18,
    unpairedItems: 0,
    revisionMismatched: 0,
    effect: 3.2,
    effectLower: -0.8,
    effectUpper: 7.1,
    effectKind: 'Difference',
    dz: 0.45,
    pValue: 0.042,
    adjustedPValue: 0.042,
    method: 'Wilcoxon signed-rank on per-question mean quality',
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
    adjustment: pairs.length > 1 ? 'Holm' : 'None',
    adjustmentNote: pairs.length > 1 ? `Holm-adjusted across ${pairs.length} tests` : 'Single comparison — no adjustment needed',
    notTestedReason: null,
    caption: null,
    pairs,
    ...overrides
  };
}

const ratio = (overrides: Partial<BenchmarkPairedTestDto> = {}): BenchmarkPairedTestDto => pair({
  effectKind: 'Ratio', effect: 0.82, effectLower: 0.71, effectUpper: 0.95, dz: null,
  direction: 'Lower', verdict: 'Faster on the same questions', ...overrides
});

function runComparison(overrides: Partial<BenchmarkPairComparisonDto> = {}): BenchmarkPairComparisonDto {
  return {
    computedAtUtc: '2026-10-03T12:00:00Z',
    subjectKind: 'Run',
    treatmentId: 2,
    baselineId: 1,
    treatmentKey: 'run:2',
    baselineKey: 'run:1',
    treatmentLabel: 'Run #2 · Model B',
    baselineLabel: 'Run #1 · Model A',
    kind: 'ModelComparison',
    kindLabel: 'Model comparison',
    explanation: 'The model differs; every key that must match agrees.',
    changedKeys: ['ModelId'],
    differences: [],
    speedDegraded: false,
    speedDegradingKeys: [],
    costDegraded: false,
    costDegradingKeys: [],
    singleRunCaveat: 'At least one entry has a single run.',
    pricingBasis: 'Current',
    measures: [
      measure('Intelligence', 'Intelligence', [pair()]),
      measure('Accuracy', 'QualityDimension', [pair({ effect: 0.4, effectLower: -0.2, effectUpper: 1, established: false, verdict: 'No difference established' })]),
      measure('Readability', 'QualityDimension', [pair()]),
      measure('Speed', 'Speed', [ratio()]),
      measure('Cost', 'Cost', [ratio({ verdict: 'Cheaper on the same questions' })])
    ],
    ...overrides
  };
}

describe('PairedTestResultComponent', () => {
  let fixture: ComponentFixture<PairedTestResultComponent>;

  beforeEach(async () => {
    localStorage.removeItem(PAIRED_RESULT_SECTIONS_STORAGE_KEY);
    await TestBed.configureTestingModule({ imports: [PairedTestResultComponent] }).compileComponents();
    fixture = TestBed.createComponent(PairedTestResultComponent);
  });

  afterEach(() => {
    localStorage.removeItem(PAIRED_RESULT_SECTIONS_STORAGE_KEY);
  });

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string): string {
    return (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function set(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  it('renders nothing without a comparison or measures', () => {
    fixture.detectChanges();
    expect(el().querySelector('.ptr')).toBeNull();
  });

  it('shows the Intelligence headline with its interval and p, then the other measures in disclosures', () => {
    set({ comparison: runComparison() });

    expect(text('.ptr-headline-title')).toBe('Intelligence Index (primary test)');
    expect(text('.ptr-headline-effect')).toBe('+3.2');
    expect(text('.ptr-headline-interval')).toBe('95 % interval −0.8 to 7.1');
    expect(text('.ptr-p')).toBe('0.04');
    // A single pair carries no adjustment line and no adjusted p.
    expect(el().querySelector('.ptr-adjusted-p')).toBeNull();
    expect(el().querySelector('.ptr-adjustment')).toBeNull();
    expect(text('.ptr-headline .ptr-verdict')).toBe('Higher on the same questions');
    expect(el().querySelector('.ptr-headline circle')?.getAttribute('fill')).toBe('currentColor');

    const sections = Array.from(el().querySelectorAll('details.ptr-section')) as HTMLDetailsElement[];
    expect(sections.map(section => section.querySelector('summary')!.textContent!.trim()))
      .toEqual(['Quality dimensions', 'Speed', 'Cost']);
    expect(sections.every(section => section.open)).toBe(true);
    expect(el().querySelectorAll('#ptr-section-dimensions tbody tr').length).toBe(2);
    expect(text('#ptr-section-dimensions tbody tr[data-measure="Accuracy"]')).toContain('No difference established');
    expect(el().querySelector('#ptr-section-dimensions tbody tr[data-measure="Accuracy"] circle')?.getAttribute('fill')).toBe('none');
    expect(text('#ptr-section-speed')).toContain('0.82× (0.71–0.95)');
    expect(text('#ptr-section-speed')).toContain('Faster on the same questions');
    expect(text('#ptr-section-cost')).toContain('Cheaper on the same questions');
    expect(text('.ptr-caveat')).toBe('At least one entry has a single run.');
    expect(text('.ptr-kind')).toBe('Model comparison: The model differs; every key that must match agrees.');
    expect(el().querySelector('.ptr-changed')).toBeNull();
  });

  it('names the changed instrument keys of a verification', () => {
    set({
      comparison: runComparison({
        kind: 'Verification', kindLabel: 'Verification of a change', explanation: '',
        changedKeys: ['CandidateSystemPromptSha256', 'ToolGuidesSha256']
      })
    });
    expect(text('.ptr-kind')).toContain('Verification of a change');
    expect(text('.ptr-changed')).toBe('Changed: CandidateSystemPromptSha256, ToolGuidesSha256');
  });

  it('says why a measure was not tested, with a dash in place of the shape', () => {
    const comparison = runComparison();
    comparison.measures[3] = measure('Speed', 'Speed', [ratio({ verdict: 'Not tested', established: false })], {
      notTestedReason: 'The speed axis is degraded: ParallelExecutionMode differs.'
    });
    set({ comparison });

    expect(text('#ptr-section-speed tbody')).toContain('Not tested: The speed axis is degraded: ParallelExecutionMode differs.');
    expect(el().querySelector('#ptr-section-speed tbody line')).not.toBeNull();
    expect(el().querySelector('#ptr-section-speed tbody circle')).toBeNull();
  });

  it('shows one pair of a larger family, with its adjusted p and adjustment note, when asked', () => {
    const family = [pair(), pair({ treatmentKey: 'run:3', effect: -1.5, adjustedPValue: 0.6, established: false, verdict: 'No difference established' })];
    set({
      measures: [measure('Intelligence', 'Intelligence', family), measure('Speed', 'Speed', [ratio(), ratio({ treatmentKey: 'run:3', effect: 1.3 })])],
      baselineKey: 'run:1',
      treatmentKey: 'run:3',
      baselineLabel: 'Model A',
      treatmentLabel: 'Model C',
      showAdjustment: true,
      showKind: false
    });

    expect(text('.ptr-subject')).toContain('Model C against Model A');
    expect(text('.ptr-headline-effect')).toBe('−1.5');
    expect(text('.ptr-adjusted-p')).toBe('0.60');
    expect(text('.ptr-headline .ptr-adjustment')).toBe('Holm-adjusted across 2 tests');
    expect(text('#ptr-section-speed tbody')).toContain('1.30×');
    expect(text('#ptr-section-speed thead')).toContain('Adjusted p');
    expect(el().querySelector('.ptr-kind')).toBeNull();
  });

  it('lists a battery Intelligence row\'s suites, and can leave the headline to its host', () => {
    const comparison = runComparison({ subjectKind: 'Battery' });
    comparison.measures[0] = measure('Intelligence', 'Intelligence', [pair({
      suites: [
        { suiteIndex: 0, suiteName: 'Gameplay Help', pairedItems: 10, weightedDifference: 3, wilcoxonPValue: 0.04, holmAdjustedPValue: 0.08, note: null },
        { suiteIndex: 1, suiteName: 'Board Reading', pairedItems: 8, weightedDifference: -1.2, wilcoxonPValue: 0.3, holmAdjustedPValue: 0.3, note: 'Two questions changed revision.' }
      ]
    })]);
    set({ comparison });

    const rows = el().querySelectorAll('.ptr-suite-table tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[1].textContent).toContain('−1.2');
    expect(rows[1].textContent).toContain('Two questions changed revision.');

    set({ showPrimary: false });
    expect(el().querySelector('.ptr-headline')).toBeNull();
    expect(el().querySelector('#ptr-section-speed')).not.toBeNull();
  });

  it('remembers which sections are open, per browser', () => {
    set({ comparison: runComparison() });
    const speed = el().querySelector('#ptr-section-speed') as HTMLDetailsElement;
    speed.open = false;
    speed.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();

    expect(JSON.parse(localStorage.getItem(PAIRED_RESULT_SECTIONS_STORAGE_KEY)!))
      .toEqual({ version: 1, open: { dimensions: true, speed: false, cost: true } });

    const again = TestBed.createComponent(PairedTestResultComponent);
    again.componentRef.setInput('comparison', runComparison());
    again.detectChanges();
    expect(((again.nativeElement as HTMLElement).querySelector('#ptr-section-speed') as HTMLDetailsElement).open).toBe(false);
    again.destroy();
  });
});
