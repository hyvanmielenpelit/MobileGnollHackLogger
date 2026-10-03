import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkPairComparisonDto,
  BenchmarkPairedMeasureDto,
  BenchmarkReportPackPricingBasis,
  BenchmarkRunDetailDto,
  BenchmarkRunPairKindDto,
  BenchmarkRunSummaryDto
} from '../../../services/admin-benchmark.service';
import { RunPairedTestComponent, isFinishedRun, runPairedOptionLabel } from './run-paired-test.component';

function summary(id: number, overrides: Partial<BenchmarkRunSummaryDto> = {}): BenchmarkRunSummaryDto {
  return {
    id,
    benchmarkSuiteId: 5,
    suiteName: 'Gameplay Help',
    testedModelDisplayNameUsed: `Model ${id}`,
    testedModelProviderUsed: 'Google',
    testedModelIdUsed: `model-${id}`,
    testedModelThinkingLevelUsed: null,
    assessorModelDisplayNameUsed: 'Assessor',
    status: 'Completed',
    startedAtUtc: `2026-09-${String(id).padStart(2, '0')}T10:00:00Z`,
    qualityIndex: 60 + id,
    totalAnswerDurationMs: 1000,
    speedMeasurementDegraded: false,
    answeredQuestionCount: 18,
    totalQuestionCount: 18,
    unansweredQuestionCount: 0,
    totalDurationMs: 1000,
    ...overrides
  } as BenchmarkRunSummaryDto;
}

function detail(id: number): BenchmarkRunDetailDto {
  return { ...summary(id), testedModelThinkingLevelUsed: 'high' } as unknown as BenchmarkRunDetailDto;
}

function kind(runId: number, value: BenchmarkRunPairKindDto['kind'], explanation = ''): BenchmarkRunPairKindDto {
  const labels: Record<string, string> = {
    ModelComparison: 'Model comparison', Verification: 'Verification of a change', Replicate: 'Replicate', NotComparable: 'Not comparable'
  };
  return {
    runId,
    kind: value,
    kindLabel: labels[value],
    changedKeys: value === 'Verification' ? ['CandidateSystemPromptSha256'] : value === 'ModelComparison' ? ['ModelId'] : [],
    explanation
  };
}

function result(): BenchmarkPairComparisonDto {
  const measure = (name: string, category: BenchmarkPairedMeasureDto['category']): BenchmarkPairedMeasureDto => ({
    measure: name, label: name === 'Intelligence' ? 'Intelligence Index' : name, category, primary: category === 'Intelligence',
    familySize: 1, adjustment: 'None', adjustmentNote: 'Single comparison — no adjustment needed', notTestedReason: null, caption: null,
    pairs: [{
      baselineKey: 'run:4', treatmentKey: 'run:10', pairedItems: 18, unpairedItems: 0, revisionMismatched: 0,
      effect: category === 'Speed' ? 0.82 : 3.2, effectLower: category === 'Speed' ? 0.71 : -0.8, effectUpper: category === 'Speed' ? 0.95 : 7.1,
      effectKind: category === 'Speed' ? 'Ratio' : 'Difference', dz: 0.45, pValue: 0.2, adjustedPValue: 0.2,
      method: 'Wilcoxon signed-rank', direction: 'None', established: false, verdict: 'No difference established',
      notTestedReason: null, note: null, suites: null
    }]
  });
  return {
    computedAtUtc: '2026-10-03T12:00:00Z', subjectKind: 'Run', treatmentId: 10, baselineId: 4,
    treatmentKey: 'run:10', baselineKey: 'run:4', treatmentLabel: 'Run #10', baselineLabel: 'Run #4',
    kind: 'Verification', kindLabel: 'Verification of a change', explanation: '', changedKeys: ['CandidateSystemPromptSha256'],
    differences: [], speedDegraded: false, speedDegradingKeys: [], costDegraded: false, costDegradingKeys: [],
    singleRunCaveat: 'At least one entry has a single run.', pricingBasis: 'Current',
    measures: [measure('Intelligence', 'Intelligence'), measure('Speed', 'Speed')]
  };
}

describe('RunPairedTestComponent', () => {
  let fixture: ComponentFixture<RunPairedTestComponent>;
  let service: MockedObject<AdminBenchmarkService>;

  // Run 10 is this run. 9 is on another suite, 8 is still running, 7 was canceled; 6, 4, 3 and 2 are
  // finished on the same suite.
  const candidates = [
    summary(2), summary(9, { benchmarkSuiteId: 6 }), summary(4), summary(10), summary(8, { status: 'Running' }),
    summary(7, { status: 'Canceled' }), summary(6), summary(3, { status: 'CompletedWithErrors' })
  ];

  beforeEach(async () => {
    service = {
      getRunPairKinds: vi.fn().mockName('AdminBenchmarkService.getRunPairKinds'),
      getRunPairedComparison: vi.fn().mockName('AdminBenchmarkService.getRunPairedComparison')
    } as unknown as MockedObject<AdminBenchmarkService>;
    service.getRunPairKinds.mockReturnValue(of([
      kind(6, 'ModelComparison', 'Model comparison: the model differs.'),
      kind(4, 'Verification', 'The same model; CandidateSystemPromptSha256 differs.'),
      kind(3, 'Replicate', ''),
      kind(2, 'NotComparable', 'Not comparable: ScoringMethodVersion differs.')
    ]));
    service.getRunPairedComparison.mockReturnValue(of(result()));
    await TestBed.configureTestingModule({
      imports: [RunPairedTestComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
    fixture = TestBed.createComponent(RunPairedTestComponent);
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

  function choose(value: string): void {
    const select = el().querySelector('#rpt-baseline') as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('offers finished runs on the same suite, newest first, this run left out', () => {
    expect(isFinishedRun(summary(1, { status: 'CompletedWithLimits' }))).toBe(true);
    expect(isFinishedRun(summary(1, { status: 'Failed' }))).toBe(false);
    expect(runPairedOptionLabel(summary(4, { testedModelThinkingLevelUsed: 'low' }))).toBe('#4 · Model 4 (low) · 64.0 · 2026-09-04');
    expect(runPairedOptionLabel(summary(4, { qualityIndex: null }))).toContain('no index');

    show({ run: detail(10), candidateRuns: candidates, active: false });
    expect(fixture.componentInstance.candidates.map(run => run.id)).toEqual([6, 4, 3, 2]);
    // The kinds are asked for only once the tab is shown.
    expect(service.getRunPairKinds).not.toHaveBeenCalled();
    expect(text('.rpt-treatment')).toBe('#10 · Model 10 (high) · 70.0');
  });

  it('groups the baselines by the kind of comparison they make, and leaves the not comparable out', () => {
    show({ run: detail(10), candidateRuns: candidates, active: true });

    expect(service.getRunPairKinds).toHaveBeenCalledWith(10, [6, 4, 3, 2]);
    const groups = Array.from(el().querySelectorAll('#rpt-baseline optgroup'));
    expect(groups.map(group => group.getAttribute('label'))).toEqual(['Model comparison', 'Verification of a change', 'Replicate']);
    expect(groups.map(group => Array.from(group.querySelectorAll('option')).map(o => (o as HTMLOptionElement).value)))
      .toEqual([['6'], ['4'], ['3']]);
    expect(text('.rpt-status')).toBe('1 finished run on this suite is not comparable with this one and is not listed.');
    expect(text('#rpt-kind')).toBe('Choose a baseline.');
    expect((el().querySelector('.rpt-compare') as HTMLButtonElement).disabled).toBe(true);

    // The same run and candidates ask once.
    show({ candidateRuns: [...candidates] });
    expect(service.getRunPairKinds).toHaveBeenCalledTimes(1);
  });

  it('names the kind of the chosen baseline and, for a verification, the changed key', () => {
    show({ run: detail(10), candidateRuns: candidates, active: true });

    choose('6');
    expect(text('#rpt-kind')).toBe('Model comparison: the model differs.');
    expect(el().querySelector('.rpt-changed')).toBeNull();

    choose('4');
    expect(text('#rpt-kind')).toBe('Verification of a change: The same model; CandidateSystemPromptSha256 differs.');
    expect(text('.rpt-changed')).toBe('Changed: CandidateSystemPromptSha256');

    choose('3');
    expect(text('#rpt-kind')).toContain('run-to-run variation');
  });

  it('compares this run with the baseline and shows every measure as one pair, unadjusted', () => {
    show({ run: detail(10), candidateRuns: candidates, active: true });
    choose('4');
    (el().querySelector('.rpt-compare') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service.getRunPairedComparison).toHaveBeenCalledWith(10, 4, BenchmarkReportPackPricingBasis.Current);
    expect(text('.ptr-headline-effect')).toBe('+3.2');
    expect(text('.ptr-headline .ptr-verdict')).toBe('No difference established');
    expect(el().querySelector('.ptr-adjusted-p')).toBeNull();
    expect(text('#rpt-result-section-speed')).toContain('0.82× (0.71–0.95)');
    expect(text('.ptr-caveat')).toContain('single run');

    // A new baseline clears the result.
    choose('6');
    expect(el().querySelector('app-paired-test-result')).toBeNull();
  });

  it('shows the server\'s refusal', () => {
    service.getRunPairedComparison.mockReturnValue(throwError(() => ({
      status: 400, error: 'Not comparable: run #6 answered "Board Reading"; a paired test needs the same suite.'
    })));
    show({ run: detail(10), candidateRuns: candidates, active: true });
    choose('6');
    (el().querySelector('.rpt-compare') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(text('.rpt-error')).toContain('a paired test needs the same suite');
    expect(el().querySelector('app-paired-test-result')).toBeNull();
  });

  it('lists every finished run ungrouped when the kinds cannot be fetched', () => {
    service.getRunPairKinds.mockReturnValue(throwError(() => ({ status: 500, error: 'Server error.' })));
    show({ run: detail(10), candidateRuns: candidates, active: true });

    expect(text('.rpt-kinds-error')).toBe('Server error.');
    const groups = Array.from(el().querySelectorAll('#rpt-baseline optgroup'));
    expect(groups.map(group => group.getAttribute('label'))).toEqual(['Finished runs on this suite']);
    expect(groups[0].querySelectorAll('option').length).toBe(4);
    choose('2');
    expect((el().querySelector('.rpt-compare') as HTMLButtonElement).disabled).toBe(false);
  });

  it('says so when no other finished run is on the suite', () => {
    show({ run: detail(10), candidateRuns: [summary(10), summary(9, { benchmarkSuiteId: 6 })], active: true });
    expect(text('.rpt-empty')).toBe('No other finished run on this suite to compare with.');
    expect(el().querySelector('#rpt-baseline')).toBeNull();
    expect(service.getRunPairKinds).not.toHaveBeenCalled();
  });

  it('starts over for another run', () => {
    show({ run: detail(10), candidateRuns: candidates, active: true });
    choose('6');
    show({ run: detail(6) });
    expect(fixture.componentInstance.baselineId).toBeNull();
    expect(service.getRunPairKinds).toHaveBeenLastCalledWith(6, [10, 4, 3, 2]);
  });
});
