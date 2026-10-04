import type { Mock, MockedObject } from 'vitest';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { of } from 'rxjs';

import {
  AdminBenchmarkService,
  BenchmarkBatteryAnalysisDto,
  BenchmarkBatteryLeaderboardDto,
  BenchmarkBatteryLeaderboardRowDto,
  BenchmarkBatteryMemberDto,
  BenchmarkBatteryRunDto,
  BenchmarkBatterySlotDto,
  BenchmarkPairComparisonDto,
  BenchmarkPairedMeasureDto
} from '../../../services/admin-benchmark.service';
import { SystemService } from '../../../services/system.service';
import { BenchmarkDownloadCenterComponent } from '../download-center/benchmark-download-center.component';
import {
  KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY,
  KEY_FIGURES_EXPORT_STORAGE_KEY
} from '../run-report-frame/key-figures-export-settings';
import { BenchmarkActiveRunMonitor } from '../state/benchmark-active-run.monitor';
import { BatteryAiReportsComponent, BatteryReportStatusChange } from './battery-ai-reports.component';
import {
  BATTERY_RUN_REPORT_HEADER_STORAGE_KEY,
  BATTERY_RUN_REPORT_IMAGE_DETAILS_STORAGE_KEY,
  BATTERY_RUN_REPORT_KEY_FIGURES_STORAGE_KEY,
  BATTERY_RUN_REPORT_TAB_STORAGE_KEY,
  BatteryRunReportDialogComponent
} from './battery-run-report-dialog.component';
import {
  BenchmarkBatteryComparison,
  BenchmarkBatteryStatisticsResult,
  BenchmarkBatterySuiteProfile
} from './battery.models';

// Fixtures and the TestBed set-up shared by the Battery Run Report dialog's spec files.

export function dto<T>(value: object): T {
  return value as T;
}

export const HASH = 'abcdef0123456789abcdef0123456789';

export function member(overrides: Partial<BenchmarkBatteryMemberDto> = {}): BenchmarkBatteryMemberDto {
  return dto<BenchmarkBatteryMemberDto>({
    memberId: 1,
    suiteIndex: 0,
    round: 1,
    runId: 101,
    runStatus: 'Completed',
    qualityIndex: 70,
    speedIndex: 55,
    origin: 'Launched',
    superseded: false,
    usable: true,
    unusableReason: null,
    guardFailure: null,
    addedAtUtc: '2026-10-01T10:00:00Z',
    runStartedAtUtc: '2026-10-01T10:00:00Z',
    runCompletedAtUtc: '2026-10-01T10:20:00Z',
    answeredQuestionCount: 10,
    totalQuestionCount: 10,
    ...overrides
  });
}

export function slot(suiteIndex: number, round: number, occupant: BenchmarkBatteryMemberDto | null): BenchmarkBatterySlotDto {
  return dto<BenchmarkBatterySlotDto>({ suiteIndex, round, member: occupant });
}

/** A completed battery run #7 of two suites, one round, members #101 and #102. */
export function batteryRun(overrides: Partial<BenchmarkBatteryRunDto> = {}): BenchmarkBatteryRunDto {
  const first = member();
  const second = member({ memberId: 2, suiteIndex: 1, runId: 102, qualityIndex: 74 });
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
    testedProvider: 'OpenAI',
    assessorLabel: 'Assessor Y',
    scoringProfileName: 'Standard Intelligence Index',
    currentSuiteIndex: null,
    currentSuitePosition: null,
    currentSuiteName: null,
    currentRound: null,
    currentRunId: null,
    slots: [slot(0, 1, first), slot(1, 1, second)],
    members: [first, second],
    latestAnalysisId: 21,
    latestAnalysisAtUtc: '2026-10-01T11:01:00Z',
    latestAnalysisComplete: true,
    comparabilityClassSha256: 'class-a',
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

export function profile(overrides: Partial<BenchmarkBatterySuiteProfile>): BenchmarkBatterySuiteProfile {
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

export function completeResult(): BenchmarkBatteryStatisticsResult {
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
      profile({ suiteIndex: 1, suiteId: 12, suiteName: 'Board Reading', weight: 0.6, index: 74, contribution: 44.4, examItemCount: 8, scoredItemCount: 8 })
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
    speed: {
      overallSpeedIndex: 61.2, speedIndexWithheldBySuiteIndex: [], pooledAnswerCount: 18,
      modelTimeP50Ms: 4200, modelTimeP90Ms: 9000, modelTimeMaxMs: 12000, modelTimeMeanMs: 5100,
      totalModelTimeMs: 91800, ttftAnswerCount: 18, ttftP50Ms: 800, ttftP90Ms: 1500, ttftMaxMs: 2100,
      degraded: false, degradedReason: null
    },
    cost: {
      available: true, withheldBySuiteIndex: [], withheldReason: null, totalCost: 3.5, passCost: 3.5,
      totalCostByRole: { Candidate: 1, Assessor: 2.5 }, passCostByRole: { Candidate: 1, Assessor: 2.5 },
      answerRowCount: 18, costPerQuestion: 0.1944, costPerIndexPoint: 0.0483, degraded: false, degradedReason: null
    },
    usage: null,
    caveats: ['Fewer than three complete battery rounds: no reproducibility figure is reported.']
  };
}

export function analysis(overrides: Partial<BenchmarkBatteryAnalysisDto> = {}): BenchmarkBatteryAnalysisDto {
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

export function leaderboardRow(overrides: Partial<BenchmarkBatteryLeaderboardRowDto>): BenchmarkBatteryLeaderboardRowDto {
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

/** Class A holds this run (#7) and #8 (Model Y) and #4 (Model X again); class B holds #5 (Model Z). */
export function leaderboard(): BenchmarkBatteryLeaderboardDto {
  return dto<BenchmarkBatteryLeaderboardDto>({
    definitionSha256: HASH,
    batteryId: 3,
    batteryName: 'Core Battery',
    classes: [
      {
        comparabilityClassSha256: 'class-a', label: 'Harness 30', harnessVersion: '30', scoringMethodVersion: 9,
        distinguishingKeys: ['HarnessVersion'],
        rows: [
          leaderboardRow({}),
          leaderboardRow({ batteryRunId: 8, testedModelConfigurationId: 6, testedModelLabel: 'Model Y', overallIndex: 68 }),
          leaderboardRow({ batteryRunId: 4, overallIndex: 71 })
        ]
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

export function comparison(): BenchmarkBatteryComparison {
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

/** One measure of a battery pair: a single pair, unadjusted. */
export function pairedMeasure(
  measure: string,
  category: BenchmarkPairedMeasureDto['category'],
  pair: Partial<BenchmarkPairedMeasureDto['pairs'][number]> = {}
): BenchmarkPairedMeasureDto {
  const ratio = category === 'Speed' || category === 'Cost';
  return {
    measure,
    label: measure === 'Intelligence' ? 'Overall Index' : measure,
    category,
    primary: category === 'Intelligence',
    familySize: 1,
    adjustment: 'None',
    adjustmentNote: 'Single comparison — no adjustment needed',
    notTestedReason: null,
    caption: null,
    pairs: [{
      baselineKey: 'battery:8',
      treatmentKey: 'battery:7',
      pairedItems: 18,
      unpairedItems: 0,
      revisionMismatched: 0,
      effect: ratio ? 0.82 : 4.4,
      effectLower: ratio ? 0.71 : 1.6,
      effectUpper: ratio ? 0.95 : 7.2,
      effectKind: ratio ? 'Ratio' : 'Difference',
      dz: ratio ? null : 0.61,
      pValue: 0.0123,
      adjustedPValue: 0.0123,
      method: ratio ? 'Wilcoxon signed-rank on log(B / A)' : 'Stratified paired sign-flip test',
      direction: ratio ? 'Lower' : 'Higher',
      established: true,
      verdict: category === 'Speed' ? 'Faster on the same questions'
        : category === 'Cost' ? 'Cheaper on the same questions' : 'Higher on the same questions',
      notTestedReason: null,
      note: null,
      suites: null,
      ...pair
    }]
  };
}

/** Battery run 7 against 8 on every measure, as `getBatteryPairedComparison` returns it. */
export function batteryPairComparison(overrides: Partial<BenchmarkPairComparisonDto> = {}): BenchmarkPairComparisonDto {
  return {
    computedAtUtc: '2026-10-03T12:00:00Z',
    subjectKind: 'Battery',
    treatmentId: 7,
    baselineId: 8,
    treatmentKey: 'battery:7',
    baselineKey: 'battery:8',
    treatmentLabel: 'Model X',
    baselineLabel: 'Model Y',
    kind: 'ModelComparison',
    kindLabel: 'Model comparison',
    explanation: 'The model differs; every key that must match agrees.',
    changedKeys: ['ModelId'],
    differences: [],
    speedDegraded: false,
    speedDegradingKeys: [],
    costDegraded: false,
    costDegradingKeys: [],
    singleRunCaveat: null,
    pricingBasis: 'Current',
    measures: [
      pairedMeasure('Intelligence', 'Intelligence'),
      pairedMeasure('Accuracy', 'QualityDimension', { effect: 0.4, effectLower: -0.2, effectUpper: 1.0, established: false, direction: 'None', verdict: 'No difference established', pValue: 0.21, adjustedPValue: 0.21 }),
      pairedMeasure('Speed', 'Speed'),
      pairedMeasure('Cost', 'Cost')
    ],
    ...overrides
  };
}

/** Stands in for `app-battery-ai-reports`, so these specs do not depend on its internals. */
@Component({ selector: 'app-battery-ai-reports', standalone: true, template: '' })
export class StubBatteryAiReportsComponent {
  @Input() batteryRun: BenchmarkBatteryRunDto | null = null;
  @Input() analysis: BenchmarkBatteryAnalysisDto | null = null;
  @Input() dialogOpen = true;
  @Output() readonly downloadsRequested = new EventEmitter<HTMLElement>();
  @Output() readonly reportStatusChange = new EventEmitter<BatteryReportStatusChange>();
}

/** Stands in for the Download Center dialog; `open` records the context. */
@Component({ selector: 'app-benchmark-download-center', standalone: true, template: '' })
export class StubDownloadCenterComponent {
  @Output() readonly closed = new EventEmitter<void>();
  readonly open = vi.fn().mockName('DownloadCenter.open');
  readonly close = vi.fn().mockName('DownloadCenter.close');
}

const STORAGE_KEYS = [
  BATTERY_RUN_REPORT_TAB_STORAGE_KEY,
  BATTERY_RUN_REPORT_HEADER_STORAGE_KEY,
  BATTERY_RUN_REPORT_KEY_FIGURES_STORAGE_KEY,
  BATTERY_RUN_REPORT_IMAGE_DETAILS_STORAGE_KEY,
  KEY_FIGURES_EXPORT_STORAGE_KEY,
  KEY_FIGURES_EXPORT_SECTIONS_STORAGE_KEY
];

export function clearBatteryRunReportStorage(): void {
  for (const key of STORAGE_KEYS) {
    try {
      localStorage.removeItem(key);
    } catch {
      // Storage unavailable.
    }
  }
}

export interface BatteryRunReportHarness {
  fixture: ComponentFixture<BatteryRunReportDialogComponent>;
  component: BatteryRunReportDialogComponent;
  service: MockedObject<AdminBenchmarkService>;
  monitor: { openBatteryDialog: Mock; armCompletionSignalsFromGesture: Mock };
  el(): HTMLElement;
  text(selector: string): string;
  click(selector: string): void;
  select(selector: string, value: string): void;
  /** Opens battery run `id` (7) and renders it. */
  open(id?: number): void;
  dialog(): HTMLDialogElement;
  downloadCenter(): StubDownloadCenterComponent;
  aiReports(): StubBatteryAiReportsComponent;
  /** The Actions popover item for `key`. */
  action(key: string): HTMLButtonElement;
}

/**
 * Configures TestBed with the mocked service, version and monitor, and the two stub children, clears
 * the remembered choices and creates the dialog.
 */
export async function configureBatteryRunReport(): Promise<BatteryRunReportHarness> {
  clearBatteryRunReportStorage();
  const service = {
    getBatteryRun: vi.fn().mockName('AdminBenchmarkService.getBatteryRun'),
    getBatteryAnalysis: vi.fn().mockName('AdminBenchmarkService.getBatteryAnalysis'),
    analyseBatteryRun: vi.fn().mockName('AdminBenchmarkService.analyseBatteryRun'),
    resumeBatteryRun: vi.fn().mockName('AdminBenchmarkService.resumeBatteryRun'),
    getBatteryLeaderboard: vi.fn().mockName('AdminBenchmarkService.getBatteryLeaderboard'),
    getBatteryPairedComparison: vi.fn().mockName('AdminBenchmarkService.getBatteryPairedComparison')
  } as unknown as MockedObject<AdminBenchmarkService>;
  service.getBatteryRun.mockReturnValue(of(batteryRun()));
  service.getBatteryAnalysis.mockReturnValue(of(analysis()));
  service.analyseBatteryRun.mockReturnValue(of(analysis()));
  service.resumeBatteryRun.mockReturnValue(of({ batteryRunId: 7 }));
  service.getBatteryLeaderboard.mockReturnValue(of(leaderboard()));
  service.getBatteryPairedComparison.mockReturnValue(of(batteryPairComparison()));
  const monitor = {
    openBatteryDialog: vi.fn().mockName('BenchmarkActiveRunMonitor.openBatteryDialog'),
    armCompletionSignalsFromGesture: vi.fn().mockName('BenchmarkActiveRunMonitor.armCompletionSignalsFromGesture')
  };
  const system = { getVersion: vi.fn().mockName('SystemService.getVersion').mockReturnValue(of('9.9.9')) };

  await TestBed.configureTestingModule({
    imports: [BatteryRunReportDialogComponent],
    providers: [
      { provide: AdminBenchmarkService, useValue: service },
      { provide: SystemService, useValue: system },
      { provide: BenchmarkActiveRunMonitor, useValue: monitor }
    ]
  }).overrideComponent(BatteryRunReportDialogComponent, {
    remove: { imports: [BatteryAiReportsComponent, BenchmarkDownloadCenterComponent] },
    add: { imports: [StubBatteryAiReportsComponent, StubDownloadCenterComponent] }
  }).compileComponents();

  const fixture = TestBed.createComponent(BatteryRunReportDialogComponent);
  const component = fixture.componentInstance;
  fixture.detectChanges();

  const el = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const harness: BatteryRunReportHarness = {
    fixture,
    component,
    service,
    monitor,
    el,
    text: (selector: string): string => (el().querySelector(selector)?.textContent ?? '').replace(/\s+/g, ' ').trim(),
    click: (selector: string): void => {
      const target = el().querySelector(selector) as HTMLElement | null;
      expect(target, selector).not.toBeNull();
      target!.click();
      fixture.detectChanges();
    },
    select: (selector: string, value: string): void => {
      const control = el().querySelector(selector) as HTMLSelectElement;
      control.value = value;
      control.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    },
    open: (id = 7): void => {
      component.open(id);
      fixture.detectChanges();
    },
    dialog: (): HTMLDialogElement => el().querySelector('#batteryRunReportDialog') as HTMLDialogElement,
    downloadCenter: (): StubDownloadCenterComponent =>
      fixture.debugElement.query(By.directive(StubDownloadCenterComponent)).componentInstance as StubDownloadCenterComponent,
    aiReports: (): StubBatteryAiReportsComponent =>
      fixture.debugElement.query(By.directive(StubBatteryAiReportsComponent)).componentInstance as StubBatteryAiReportsComponent,
    action: (key: string): HTMLButtonElement =>
      el().querySelector(`#brr-actions-popover [data-action="${key}"]`) as HTMLButtonElement
  };
  return harness;
}

/** Closes the dialog and any dialog nested in it, and forgets the remembered choices. */
export function tearDownBatteryRunReport(harness: BatteryRunReportHarness | undefined): void {
  if (harness) {
    harness.el().querySelectorAll('dialog').forEach(d => {
      if ((d as HTMLDialogElement).open) (d as HTMLDialogElement).close();
    });
    harness.component.close();
  }
  clearBatteryRunReportStorage();
}
