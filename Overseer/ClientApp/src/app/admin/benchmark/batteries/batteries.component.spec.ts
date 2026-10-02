import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAnalysisDto,
  BenchmarkBatteryDto,
  BenchmarkBatteryLeaderboardDto,
  BenchmarkBatteryLeaderboardRowDto,
  BenchmarkBatteryRunDto,
  BenchmarkSuiteDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkBatteriesComponent } from './batteries.component';
import {
  BenchmarkBatteryComparison,
  BenchmarkBatteryStatisticsResult,
  BenchmarkBatterySuiteProfile
} from './battery.models';

function dto<T>(value: object): T {
  return value as T;
}

const HASH = 'abcdef0123456789abcdef0123456789';

function battery(overrides: Partial<BenchmarkBatteryDto> = {}): BenchmarkBatteryDto {
  return dto<BenchmarkBatteryDto>({
    id: 3,
    name: 'Core Battery',
    description: null,
    weightingScheme: 'DifficultyMass',
    weightingSchemeLabel: 'Questions and difficulty',
    revision: 2,
    definitionSha256: HASH,
    isArchived: false,
    brokenSuiteNames: [],
    validationErrors: [],
    createdByUserName: 'admin',
    createdAtUtc: '2026-09-01T00:00:00Z',
    modifiedAtUtc: '2026-09-02T00:00:00Z',
    batteryRunCount: 2,
    hasActiveBatteryRun: false,
    suites: [
      { index: 0, suiteId: 11, suiteName: 'Gameplay Help', deleted: false, customWeight: null, questionCount: 10, assessedQuestionCount: 10, difficultyFullyAssessed: true, difficultyMass: 400 },
      { index: 1, suiteId: 12, suiteName: 'Board Reading', deleted: false, customWeight: null, questionCount: 8, assessedQuestionCount: 8, difficultyFullyAssessed: true, difficultyMass: 600 }
    ],
    weightPreviews: [
      { scheme: 'DifficultyMass', schemeLabel: 'Questions and difficulty', declared: true, weights: [0.4, 0.6] },
      { scheme: 'Equal', schemeLabel: 'Equal per suite', declared: false, weights: [0.5, 0.5] }
    ],
    ...overrides
  });
}

function run(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
  return dto<BenchmarkBatteryRunDto>({
    id: 7,
    batteryId: 3,
    batteryName: 'Core Battery',
    definitionRevision: 2,
    definitionSha256: HASH,
    weightingScheme: 'DifficultyMass',
    suites: [
      { index: 0, suiteId: 11, suiteName: 'Gameplay Help', customWeight: null },
      { index: 1, suiteId: 12, suiteName: 'Board Reading', customWeight: null }
    ],
    suiteCount: 2,
    runsPerSuite: 1,
    requestedMemberCount: 2,
    completedMemberCount: 2,
    failedMemberCount: 0,
    completedSuiteCount: 2,
    status: 'Completed',
    stopReason: null,
    stopReasonText: null,
    allowCapWait: false,
    resumable: false,
    isDriving: false,
    startedAtUtc: '2026-10-01T10:00:00Z',
    completedAtUtc: '2026-10-01T11:00:00Z',
    lastProgressAtUtc: null,
    errorMessage: null,
    startedByUserName: 'admin',
    testedModelConfigurationId: 5,
    testedModelLabel: 'Model X',
    currentSuiteIndex: null,
    currentSuitePosition: null,
    currentSuiteName: null,
    currentRound: null,
    currentRunId: null,
    slots: [],
    members: [],
    latestAnalysisId: 21,
    latestAnalysisAtUtc: '2026-10-01T11:01:00Z',
    latestAnalysisComplete: true,
    overallIndex: 72.4,
    overallIndexHalfWidth: 5.1,
    overallIndexLower: 67.3,
    overallIndexUpper: 77.5,
    overallSpeedIndex: 61.2,
    totalCost: 3.5,
    analysisStale: false,
    analysisHasExcludedMembers: false,
    ...overrides
  });
}

function profile(overrides: Partial<BenchmarkBatterySuiteProfile>): BenchmarkBatterySuiteProfile {
  return {
    suiteIndex: 0, suiteId: 11, suiteName: 'Gameplay Help', complete: true, weight: 0.4, countWeight: 0.55,
    examItemCount: 10, difficultyMass: 400, expectedQuestionCount: 10, examIncomplete: false, scoredItemCount: 10,
    usableMemberCount: 1, index: 70, contribution: 28, itemSamplingStandardError: 3, reproducibilityStandardError: null,
    combinedHalfWidth: 5.9, combinedLower: 64.1, combinedUpper: 75.9, combinedIntervalTruncated: false,
    identityHolds: true, runIndices: [], meanSpeedIndex: 60, criticalErrorRate: 0.1, totalCost: 1.5,
    meanCostPerRun: 1.5, statistics: null,
    ...overrides
  };
}

function completeResult(): BenchmarkBatteryStatisticsResult {
  return {
    methodVersion: 1,
    complete: true,
    completedSuiteCount: 2,
    suiteCount: 2,
    scheme: 'DifficultyMass',
    weights: [0.4, 0.6],
    countWeights: [0.55, 0.45],
    suiteMasses: [{ itemCount: 10, difficultyMass: 400 }, { itemCount: 8, difficultyMass: 600 }],
    excludedMembers: [],
    pooledIdentityHolds: true,
    overallIndex: {
      pointEstimate: 72.4,
      itemSamplingStandardError: 2.4,
      effectiveDegreesOfFreedom: 15.6,
      itemSamplingCriticalValue: 2.131,
      itemSamplingHalfWidth: 5.1,
      itemSamplingWithheldBySuiteIndex: [],
      reproducibilityStandardError: null,
      reproducibilityDegreesOfFreedom: null,
      reproducibilityCriticalValue: null,
      reproducibilityHalfWidth: null,
      reproducibilitySource: 'NotAvailable',
      roundCount: 1,
      perRoundIndices: [],
      combinedHalfWidth: 5.1,
      combinedLower: 67.3,
      combinedUpper: 77.5,
      combinedIntervalTruncated: false
    },
    suites: [
      profile({}),
      profile({ suiteIndex: 1, suiteId: 12, suiteName: 'Board Reading', weight: 0.6, index: 74, contribution: 44.4 })
    ],
    betweenSuiteStandardDeviation: 2.8,
    betweenSuiteRange: 4,
    weightingSensitivity: [
      { scheme: 'DifficultyMass', declared: true, weights: [0.4, 0.6], index: 72.4 },
      { scheme: 'ItemCount', declared: false, weights: [0.55, 0.45], index: 71.8 },
      { scheme: 'Equal', declared: false, weights: [0.5, 0.5], index: 72 }
    ],
    leaveOneSuiteOut: [
      { suiteIndex: 0, suiteName: 'Gameplay Help', index: 74, change: 1.6 },
      { suiteIndex: 1, suiteName: 'Board Reading', index: 70, change: -2.4 }
    ],
    dimensions: [{ dimension: 'Accuracy', mean: 75.5, withheldBySuiteIndex: [] }],
    criticalErrorRate: 0.08,
    speed: null,
    cost: null,
    usage: null,
    caveats: ['Fewer than three complete battery rounds: no reproducibility figure is reported.']
  };
}

function analysis(overrides: Partial<BenchmarkBatteryAnalysisDto> = {}): BenchmarkBatteryAnalysisDto {
  return dto<BenchmarkBatteryAnalysisDto>({
    id: 21,
    batteryRunId: 7,
    batteryName: 'Core Battery',
    computedAtUtc: '2026-10-01T11:01:00Z',
    memberRunIds: [101, 102],
    runCount: 2,
    definitionSha256: HASH,
    comparabilityClassSha256: 'class-a',
    complete: true,
    harnessVersion: '30',
    scoringMethodVersion: 9,
    stale: false,
    comparedWithBatteryRunId: null,
    comparedWithBatteryName: null,
    result: completeResult(),
    comparison: null,
    excludedMembers: [],
    ...overrides
  });
}

function leaderboardRow(overrides: Partial<BenchmarkBatteryLeaderboardRowDto>): BenchmarkBatteryLeaderboardRowDto {
  return dto<BenchmarkBatteryLeaderboardRowDto>({
    batteryRunId: 7, batteryId: 3, batteryName: 'Core Battery', definitionRevision: 2, analysisId: 21,
    computedAtUtc: '2026-10-01T11:01:00Z', testedModelConfigurationId: 5, testedModelLabel: 'Model X',
    status: 'Completed', runsPerSuite: 1, suiteCount: 2, completedSuiteCount: 2, complete: true,
    comparabilityClassSha256: 'class-a', harnessVersion: '30', scoringMethodVersion: 9,
    overallIndex: 72.4, overallIndexHalfWidth: 5.1, overallIndexLower: 67.3, overallIndexUpper: 77.5,
    overallSpeedIndex: 61.2, totalCost: 3.5, passCost: 3.5,
    ...overrides
  });
}

function leaderboard(): BenchmarkBatteryLeaderboardDto {
  return dto<BenchmarkBatteryLeaderboardDto>({
    definitionSha256: HASH,
    batteryId: 3,
    batteryName: 'Core Battery',
    classes: [
      {
        comparabilityClassSha256: 'class-a', label: 'Harness 30', harnessVersion: '30', scoringMethodVersion: 9,
        distinguishingKeys: ['HarnessVersion'],
        rows: [leaderboardRow({}), leaderboardRow({ batteryRunId: 8, testedModelConfigurationId: 6, testedModelLabel: 'Model Y', overallIndex: 68 })]
      },
      {
        comparabilityClassSha256: 'class-b', label: 'Harness 29', harnessVersion: '29', scoringMethodVersion: 9,
        distinguishingKeys: ['HarnessVersion'],
        rows: [leaderboardRow({ batteryRunId: 5, testedModelConfigurationId: 8, testedModelLabel: 'Model Z', comparabilityClassSha256: 'class-b' })]
      }
    ],
    incomplete: [leaderboardRow({ batteryRunId: 9, complete: false, completedSuiteCount: 1, testedModelLabel: 'Model W', overallIndex: null })]
  });
}

function comparison(): BenchmarkBatteryComparison {
  return {
    methodVersion: 1,
    baselineOverallIndex: 68,
    treatmentOverallIndex: 72.4,
    suites: [
      { suiteIndex: 0, suiteName: 'Gameplay Help', weight: 0.4, pairedItemCount: 10, weightedDifference: 3, weightedDifferenceStandardError: 1.2, wilcoxonPValue: 0.04, holmAdjustedPValue: 0.08, comparison: null, note: null },
      { suiteIndex: 1, suiteName: 'Board Reading', weight: 0.6, pairedItemCount: 8, weightedDifference: 5.3, weightedDifferenceStandardError: 2, wilcoxonPValue: 0.02, holmAdjustedPValue: 0.04, comparison: null, note: null }
    ],
    pairedItemCount: 18,
    compositeDifference: 4.4,
    compositeWithheldBySuiteIndex: [],
    compositeStandardError: 1.3,
    standardErrorWithheldBySuiteIndex: [],
    compositeDegreesOfFreedom: 14.2,
    compositeCriticalValue: 2.145,
    compositeConfidenceHalfWidth: 2.8,
    compositeConfidenceLower: 1.6,
    compositeConfidenceUpper: 7.2,
    randomizationPValue: 0.0123,
    randomizationMethod: 'Exact',
    monteCarloResamples: null,
    monteCarloStandardError: null,
    seed: null,
    notes: []
  };
}

describe('BenchmarkBatteriesComponent', () => {
  let fixture: ComponentFixture<BenchmarkBatteriesComponent>;
  let component: BenchmarkBatteriesComponent;
  let service: MockedObject<AdminBenchmarkService>;

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function text(selector: string): string {
    return (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function click(selector: string): void {
    const target = el().querySelector(selector) as HTMLElement | null;
    expect(target, selector).not.toBeNull();
    target!.click();
    fixture.detectChanges();
  }

  function select(selector: string, value: string): void {
    const control = el().querySelector(selector) as HTMLSelectElement;
    control.value = value;
    control.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function create(): void {
    fixture = TestBed.createComponent(BenchmarkBatteriesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    service = {
      getBatteries: vi.fn().mockName("AdminBenchmarkService.getBatteries"),
      getSuites: vi.fn().mockName("AdminBenchmarkService.getSuites"),
      getBatteryRuns: vi.fn().mockName("AdminBenchmarkService.getBatteryRuns"),
      getBatteryRun: vi.fn().mockName("AdminBenchmarkService.getBatteryRun"),
      getBatteryAnalysis: vi.fn().mockName("AdminBenchmarkService.getBatteryAnalysis"),
      analyseBatteryRun: vi.fn().mockName("AdminBenchmarkService.analyseBatteryRun"),
      getBatteryLeaderboard: vi.fn().mockName("AdminBenchmarkService.getBatteryLeaderboard"),
      getBatteryReportUrl: vi.fn().mockName("AdminBenchmarkService.getBatteryReportUrl"),
      archiveBattery: vi.fn().mockName("AdminBenchmarkService.archiveBattery"),
      deleteBattery: vi.fn().mockName("AdminBenchmarkService.deleteBattery"),
      createBattery: vi.fn().mockName("AdminBenchmarkService.createBattery"),
      updateBattery: vi.fn().mockName("AdminBenchmarkService.updateBattery"),
      getQuestions: vi.fn().mockName("AdminBenchmarkService.getQuestions")
    } as unknown as MockedObject<AdminBenchmarkService>;
    service.getBatteries.mockReturnValue(of([battery()]));
    service.getSuites.mockReturnValue(of([dto<BenchmarkSuiteDto>({ id: 11, name: 'Gameplay Help', questionCount: 10, assessedQuestionCount: 10, difficultyFullyAssessed: true })]));
    service.getBatteryRuns.mockReturnValue(of([run(), run({ id: 6, batteryName: 'Old Battery', batteryId: null, testedModelLabel: 'Model Y' })]));
    service.getBatteryRun.mockReturnValue(of(run()));
    service.getBatteryAnalysis.mockReturnValue(of(analysis()));
    service.analyseBatteryRun.mockReturnValue(of(analysis()));
    service.getBatteryLeaderboard.mockReturnValue(of(leaderboard()));
    service.getBatteryReportUrl.mockImplementation((id: number) => `/api/admin/benchmark/batteries/runs/${id}/report`);
    service.archiveBattery.mockImplementation((id: number, archived?: boolean) => of(battery({ id, isArchived: archived ?? true })));
    service.deleteBattery.mockReturnValue(of(void 0));
    service.getQuestions.mockReturnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [BenchmarkBatteriesComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
  });

  afterEach(() => {
    el().querySelectorAll('dialog').forEach(d => { if ((d as HTMLDialogElement).open) (d as HTMLDialogElement).close(); });
  });

  it('creates and loads batteries, suites and battery runs', () => {
    create();
    expect(component).toBeTruthy();
    expect(service.getBatteries).toHaveBeenCalled();
    expect(service.getSuites).toHaveBeenCalled();
    expect(service.getBatteryRuns).toHaveBeenCalled();
  });

  it('shows the empty states', () => {
    service.getBatteries.mockReturnValue(of([]));
    service.getBatteryRuns.mockReturnValue(of([]));
    create();

    expect(text('.bb-empty-batteries')).toContain('No batteries yet');
    expect(text('.bb-empty-runs')).toContain('No battery runs yet');
    expect(el().querySelector('.bb-analysis')).toBeNull();
  });

  it('lists battery cards with suites, scheme and declared weights', () => {
    create();
    const card = el().querySelector('.bb-card') as HTMLElement;
    const cardText = card.textContent!.replace(/\s+/g, ' ');
    expect(cardText).toContain('Core Battery');
    expect(cardText).toContain('Questions and difficulty');
    expect(cardText).toContain('Revision 2');
    expect(cardText).toContain('Gameplay Help');
    expect(cardText).toContain('40.0 %');
    expect(cardText).toContain('60.0 %');
  });

  it('filters the battery runs by battery', () => {
    create();
    expect(el().querySelectorAll('.bb-runs-table tbody tr[data-run-id]').length).toBe(2);
    select('#bb-f-battery', 'Old Battery');
    const rows = el().querySelectorAll('.bb-runs-table tbody tr[data-run-id]');
    expect(rows.length).toBe(1);
    expect(rows[0].getAttribute('data-run-id')).toBe('6');
  });

  it('shows a complete analysis with its headline, uncertainty, profile and sensitivity', () => {
    create();
    click('tr[data-run-id="7"] .bb-view-analysis');

    expect(service.getBatteryAnalysis).toHaveBeenCalledWith(7);
    expect(text('.bb-headline-value')).toBe('72.4 ± 5.1');
    expect(text('.bb-headline-interval')).toContain('[67.3, 77.5]');
    expect(text('.bb-uncertainty')).toContain('15.6');
    expect(text('.bb-uncertainty')).toContain('2.131');
    expect(el().querySelectorAll('.bb-profile-table tbody tr').length).toBe(2);
    expect(text('.bb-sensitivity-table')).toContain('Questions only');
    expect(text('.bb-sensitivity-table')).toContain('Sensitivity');
    expect(text('.bb-loo-table')).toContain('−2.4');
    expect(el().querySelector('.bb-recompute-callout')).toBeNull();
  });

  it('shows an incomplete analysis without a headline and calls out Recompute', () => {
    const incomplete = completeResult();
    incomplete.complete = false;
    incomplete.completedSuiteCount = 1;
    incomplete.overallIndex = null;
    incomplete.suites = [incomplete.suites[0], { ...incomplete.suites[1], complete: false, index: null }];
    service.getBatteryAnalysis.mockReturnValue(of(analysis({
      complete: false,
      result: incomplete,
      excludedMembers: [{ suiteIndex: 1, round: 1, runId: 102, reason: 'index withheld (a question failed at the provider)' }]
    })));
    create();
    click('tr[data-run-id="7"] .bb-view-analysis');

    expect(text('.bb-headline-value')).toBe('Incomplete (1 of 2 suites)');
    expect(el().querySelector('.bb-uncertainty')).toBeNull();
    expect(text('.bb-excluded')).toContain('Board Reading, round 1, run #102');
    expect(text('.bb-excluded')).toContain('index withheld');
    expect(text('.bb-recompute-callout')).toContain('Recompute');

    click('.bb-recompute');
    expect(service.analyseBatteryRun).toHaveBeenCalledWith(7);
  });

  it('offers Compute when no analysis exists', () => {
    service.getBatteryAnalysis.mockReturnValue(of(null));
    create();
    click('tr[data-run-id="7"] .bb-view-analysis');

    expect(text('.bb-no-analysis')).toContain('No analysis has been computed');
    expect(text('.bb-recompute')).toBe('Compute');
    expect(el().querySelector('.bb-download-report')?.getAttribute('aria-disabled')).toBe('true');
  });

  it('downloads the report of the selected battery run', () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(undefined as any);
    create();
    click('tr[data-run-id="7"] .bb-view-analysis');
    click('.bb-download-report');
    expect(open).toHaveBeenCalledWith('/api/admin/benchmark/batteries/runs/7/report', '_blank');
  });

  it('shows one ranked table per comparability class and the incomplete runs apart', () => {
    create();
    click('tr[data-run-id="7"] .bb-view-analysis');

    expect(service.getBatteryLeaderboard).toHaveBeenCalledWith(HASH);
    expect(el().querySelectorAll('.bb-leaderboard-table').length).toBe(2);
    expect(text('.bb-overlap-note')).toContain('Overlapping intervals are not a ranking');
    expect(text('.bb-incomplete-table')).toContain('Model W');
    expect(text('.bb-incomplete-table')).toContain('1/2');
  });

  it('shows the refusal text when a comparison is refused', () => {
    create();
    click('tr[data-run-id="7"] .bb-view-analysis');
    service.analyseBatteryRun.mockReturnValue(throwError(() => ({
      status: 400,
      error: 'Not comparable: HarnessVersion and CandidateSystemPromptSha256 differ.'
    })));

    select('#bb-compare-baseline', '5');
    select('#bb-compare-treatment', '8');
    expect(text('#bb-compare-kind')).toContain('probably be refused');
    click('.bb-compare-btn');

    expect(service.analyseBatteryRun).toHaveBeenCalledWith(8, 5);
    expect(text('.bb-compare-refusal')).toContain('HarnessVersion and CandidateSystemPromptSha256 differ');
  });

  it('shows D, its interval, the randomization p and Holm-adjusted per-suite p', () => {
    create();
    click('tr[data-run-id="7"] .bb-view-analysis');
    service.analyseBatteryRun.mockReturnValue(of(analysis({ batteryRunId: 7, comparedWithBatteryRunId: 8, comparison: comparison() })));

    select('#bb-compare-baseline', '8');
    select('#bb-compare-treatment', '7');
    expect(text('#bb-compare-kind')).toContain('Model comparison');
    click('.bb-compare-btn');

    expect(service.analyseBatteryRun).toHaveBeenCalledWith(7, 8);
    expect(text('.bb-compare-d')).toBe('+4.4');
    expect(text('.bb-compare-headline')).toContain('[1.6, 7.2]');
    expect(text('.bb-compare-facts')).toContain('0.012');
    expect(text('.bb-compare-facts')).toContain('Exact enumeration');
    const rows = el().querySelectorAll('.bb-compare-table tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[1].textContent).toContain('0.040');
  });

  it('emits openBatteryRun from Show progress', () => {
    create();
    const opened = vi.fn().mockName('opened');
    component.openBatteryRun.subscribe(opened);
    click('tr[data-run-id="7"] .bb-show-progress');
    expect(opened).toHaveBeenCalledWith(7);
  });

  it('archives a battery and emits batteriesChanged', () => {
    create();
    const changed = vi.fn().mockName('changed');
    component.batteriesChanged.subscribe(changed);
    click('.bb-card .bb-archive');

    expect(service.archiveBattery).toHaveBeenCalledWith(3, true);
    expect(changed).toHaveBeenCalled();
  });

  it('deletes a battery after confirmation', () => {
    create();
    const changed = vi.fn().mockName('changed');
    component.batteriesChanged.subscribe(changed);
    click('.bb-card .bb-delete');
    expect(text('#bbDeleteTitle')).toBe('Delete battery?');

    click('.bb-confirm-delete');
    expect(service.deleteBattery).toHaveBeenCalledWith(3);
    expect(changed).toHaveBeenCalled();
    expect(el().querySelector('.bb-card')).toBeNull();
  });

  it('refuses to delete a battery with a battery run in progress', () => {
    service.getBatteries.mockReturnValue(of([battery({ hasActiveBatteryRun: true })]));
    create();
    const deleteButton = el().querySelector('.bb-card .bb-delete') as HTMLButtonElement;
    expect(deleteButton.getAttribute('aria-disabled')).toBe('true');
    deleteButton.click();
    fixture.detectChanges();
    expect((el().querySelector('.bb-delete-dialog') as HTMLDialogElement).open).toBe(false);
  });

  it('focuses the analysis heading from showAnalysis', () => {
    create();
    component.showAnalysis(7);
    fixture.detectChanges();
    expect(document.activeElement?.id).toBe('bb-analysis-title');
  });
});
