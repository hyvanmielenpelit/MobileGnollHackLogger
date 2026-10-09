/**
 * Shared builders and fakes of the Chat Consistency specs: records shaped as the server sends them,
 * with overrides, and the providers a Chat Consistency component needs under TestBed.
 */

import { EnvironmentProviders, Provider } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideCharts } from 'ng2-charts';

import { APP_CHART_REGISTRABLES } from '../../../chart-registrables';
import { SystemAiConfigDto } from '../../../services/admin.service';
import {
  BenchmarkReportAudience,
  BenchmarkReportPackJobDto,
  BenchmarkRunReportDocumentsStatus,
  BenchmarkRunReportJobDto
} from '../../../services/admin-benchmark.service';
import { BenchmarkShellBridge } from '../state/benchmark-shell-bridge.service';
import { BenchmarkViewSync } from '../state/benchmark-view-sync.service';
import { BenchmarkWorkspaceStore } from '../state/benchmark-workspace.store';
import {
  CcAnalysisResult,
  CcAnalysisSummary,
  CcAnnotation,
  CcAttributionResult,
  CcBatteryRunRow,
  CcBatteryTimelinePoint,
  CcComparisonSets,
  CcEndpointResult,
  CcEvent,
  CcModelAxis,
  CcProtocol,
  CcRegradeEstimate,
  CcRegradeJob,
  CcReportEstimate,
  CcRunRow,
  CcRunSelectionView,
  CcSubject,
  CcTimeline,
  CcTimelinePoint,
  CcUnitView
} from './chat-consistency.models';

/** The API prefix, spelled out so a spec fails if the service moves it. */
export const CC_API = '/api/admin/benchmark/chat-consistency';

/** The providers of a Chat Consistency component spec: HTTP testing, charts and the Benchmark state. */
export function chatConsistencyTestProviders(): (Provider | EnvironmentProviders)[] {
  return [
    provideHttpClient(),
    provideHttpClientTesting(),
    provideCharts({ registerables: APP_CHART_REGISTRABLES }),
    BenchmarkShellBridge,
    BenchmarkViewSync,
    BenchmarkWorkspaceStore
  ];
}

export function ccAxis(overrides: Partial<CcModelAxis> = {}): CcModelAxis {
  return {
    key: 'openai/gpt-5|high',
    displayName: 'GPT-5 high',
    provider: 'OpenAI',
    modelId: 'gpt-5',
    thinkingLevel: 'high',
    serviceTier: null,
    runCount: 6,
    telemetryRunCount: 4,
    firstRunAtUtc: '2026-09-01T08:00:00Z',
    lastRunAtUtc: '2026-10-01T08:00:00Z',
    latestRunId: 106,
    suiteNames: ['Board Suite'],
    batteryRunCount: 0,
    ...overrides
  };
}

export function ccPoint(runId: number, startedAtUtc: string, overrides: Partial<CcTimelinePoint> = {}): CcTimelinePoint {
  return {
    runId,
    startedAtUtc,
    suiteName: 'Board Suite',
    suiteId: 5,
    harnessVersion: '30',
    status: 'completed',
    isLegacy: false,
    isAnchor: false,
    qualityIndex: 72,
    nativeMeanQuality: 71.5,
    commonGraderQuality: [],
    medianTimeToFirstAnswerTextMs: 2400,
    medianStreamingRate: 40,
    streamingRateEstimated: false,
    medianModelTimeMs: 9000,
    latencyLabel: 'telemetry',
    outputTokensPerAnswer: 1200,
    toolCallsPerAnswer: 3.5,
    costPerQuestionUsd: 0.015,
    terminalFailureRate: 0,
    timeoutRate: 0,
    emptyAnswerRate: 0,
    refusalRate: 0,
    toolBudgetExhaustedRate: 0,
    servedModelIds: [{ modelId: 'gpt-5-2026-08', callCount: 40 }],
    strata: ['weekday 08–12 UTC'],
    strataEstimated: false,
    answerCount: 20,
    maxParallelQuestions: 1,
    ...overrides
  };
}

/**
 * A complete battery run of *Two initial suites* (revision 1, {@link CC_BATTERY_SET_KEY}) as a timeline
 * point, its members runs `batteryRunId * 100 + 1` and `+ 2` unless `overrides` names them, with an
 * Overall Index of 80.
 */
export function ccBatteryPoint(
  batteryRunId: number,
  startedAtUtc: string,
  overrides: Partial<CcBatteryTimelinePoint> = {}
): CcBatteryTimelinePoint {
  return {
    ...ccPoint(batteryRunId, startedAtUtc, { suiteName: 'Two initial suites', suiteId: null, qualityIndex: null, answerCount: 40 }),
    setKey: CC_BATTERY_SET_KEY,
    batteryName: 'Two initial suites',
    definitionRevision: 1,
    completedAtUtc: new Date(Date.parse(startedAtUtc) + 60 * 60_000).toISOString(),
    batteryStatus: 'completed',
    suiteCount: 2,
    complete: true,
    incompleteReason: null,
    memberRunIds: [batteryRunId * 100 + 1, batteryRunId * 100 + 2],
    overallIndex: 80,
    overallIndexNote: null,
    ...overrides
  };
}

export function ccRunRow(runId: number, startedAtUtc: string, overrides: Partial<CcRunRow> = {}): CcRunRow {
  return {
    runId,
    startedAtUtc,
    suiteName: 'Board Suite',
    harnessVersion: '30',
    scoringMethodVersion: 4,
    status: 'completed',
    isLegacy: false,
    isAnchor: false,
    eligibility: [
      { axis: 'quality', eligible: true, segment: 1, reason: null },
      { axis: 'speedTelemetry', eligible: true, segment: 1, reason: null },
      { axis: 'speedLegacy', eligible: true, segment: 1, reason: null },
      { axis: 'work', eligible: true, segment: 1, reason: null },
      { axis: 'cost', eligible: true, segment: 1, reason: null }
    ],
    regradeCoverage: [],
    matchedControlRunIds: [],
    servedModelIds: [{ modelId: 'gpt-5-2026-08', callCount: 40 }],
    suiteId: 5,
    suiteKey: 'id:5',
    batteryRunId: null,
    batteryName: null,
    batterySuitePosition: null,
    batterySuiteCount: null,
    ...overrides
  };
}

export function ccEvent(overrides: Partial<CcEvent> = {}): CcEvent {
  return {
    atUtc: '2026-09-15T00:00:00Z',
    kind: 'ToolGuidesSha256',
    label: 'tool guides edited on 2026-09-15',
    from: 'abc',
    to: 'def',
    runId: 103,
    previousRunId: 102,
    subjectKey: 'openai/gpt-5|high',
    inTargetSeries: true,
    ...overrides
  };
}

export function ccAnnotation(id: number, overrides: Partial<CcAnnotation> = {}): CcAnnotation {
  return {
    id,
    atUtc: '2026-09-20T12:00:00Z',
    provider: 'OpenAI',
    modelId: 'gpt-5',
    kind: 'modelRelease',
    text: 'New snapshot announced',
    sourceUrl: null,
    createdAtUtc: '2026-09-20T13:00:00Z',
    ...overrides
  };
}

/** Six runs of the subject, two a week from 2026-09-01, the third one legacy. */
export function ccTimeline(overrides: Partial<CcTimeline> = {}): CcTimeline {
  return {
    subject: {
      key: 'openai/gpt-5|high', displayName: 'GPT-5 high', provider: 'OpenAI', modelId: 'gpt-5',
      thinkingLevel: 'high', serviceTier: null, configurationId: 30
    },
    fromUtc: null,
    toUtc: null,
    points: [
      ccPoint(101, '2026-09-01T08:00:00Z', { qualityIndex: 71 }),
      ccPoint(102, '2026-09-05T08:00:00Z', { qualityIndex: 73 }),
      ccPoint(103, '2026-09-12T08:00:00Z', {
        qualityIndex: 74, isLegacy: true, medianTimeToFirstAnswerTextMs: null, medianStreamingRate: null, latencyLabel: 'legacy proxy'
      }),
      ccPoint(104, '2026-09-20T08:00:00Z', { qualityIndex: 72 }),
      ccPoint(105, '2026-09-26T08:00:00Z', { qualityIndex: 71 }),
      ccPoint(106, '2026-10-01T08:00:00Z', { qualityIndex: 73 })
    ],
    batteryPoints: [],
    events: [ccEvent()],
    annotations: [ccAnnotation(1)],
    priceCard: {
      available: true, source: 'current configuration pricing', runId: null, inputPerMillion: 1.25, outputPerMillion: 10,
      cachedInputPerMillion: null, cacheWritePerMillion: null, asOf: null
    },
    ...overrides
  };
}

/** The run table of {@link ccTimeline}: run 103 is legacy and not eligible for telemetry speed. */
export function ccRunRows(): CcRunRow[] {
  return [
    ccRunRow(106, '2026-10-01T08:00:00Z', { matchedControlRunIds: [206] }),
    ccRunRow(105, '2026-09-26T08:00:00Z', { matchedControlRunIds: [205] }),
    ccRunRow(104, '2026-09-20T08:00:00Z'),
    ccRunRow(103, '2026-09-12T08:00:00Z', {
      isLegacy: true,
      eligibility: [
        { axis: 'quality', eligible: true, segment: 1, reason: null },
        { axis: 'speedTelemetry', eligible: false, segment: null, reason: 'No call telemetry' },
        { axis: 'speedLegacy', eligible: true, segment: 1, reason: null },
        { axis: 'work', eligible: true, segment: 1, reason: null },
        { axis: 'cost', eligible: true, segment: 1, reason: null }
      ]
    }),
    ccRunRow(102, '2026-09-05T08:00:00Z', { matchedControlRunIds: [202] }),
    ccRunRow(101, '2026-09-01T08:00:00Z', { matchedControlRunIds: [201] })
  ];
}

/** The second suite of the fixtures. */
const WIKI_SUITE: Partial<CcRunRow> = { suiteName: 'Wiki Suite', suiteId: 6, suiteKey: 'id:6' };

/** `count` runs #1001 onward, one a day from 2026-08-01, newest first; for the card list's batches. */
export function ccManyRunRows(count: number): CcRunRow[] {
  return Array.from({ length: count }, (_, index) => {
    const day = new Date(Date.UTC(2026, 7, 1 + index, 8)).toISOString();
    return ccRunRow(1001 + index, day, index % 2 === 0 ? {} : WIKI_SUITE);
  }).reverse();
}

/** The battery definition of the battery fixtures: *Two initial suites*, revision 1, Board Suite then Wiki Suite. */
export const CC_BATTERY_SET_KEY = `battery:${'c'.repeat(64)}`;

/** A battery run of the subject, complete, with one member per suite: run `memberIds[k]` holds suite k + 1. */
export function ccBatteryRunRow(
  batteryRunId: number,
  startedAtUtc: string,
  memberIds: readonly number[],
  overrides: Partial<CcBatteryRunRow> = {}
): CcBatteryRunRow {
  const start = Date.parse(startedAtUtc);
  const members = memberIds.map((runId, index) => ccRunRow(runId, new Date(start + index * 30 * 60_000).toISOString(), {
    ...(index % 2 === 0 ? {} : WIKI_SUITE),
    harnessVersion: '54',
    batteryRunId,
    batteryName: 'Two initial suites',
    batterySuitePosition: index + 1,
    batterySuiteCount: 2
  }));
  return {
    batteryRunId,
    batteryId: 3,
    batteryName: 'Two initial suites',
    definitionSha256: 'c'.repeat(64),
    definitionRevision: 1,
    setKey: CC_BATTERY_SET_KEY,
    startedAtUtc,
    completedAtUtc: new Date(start + memberIds.length * 30 * 60_000).toISOString(),
    status: 'completed',
    suiteCount: 2,
    complete: true,
    incompleteReason: null,
    harnessVersions: ['54'],
    members,
    eligibility: [
      { axis: 'quality', eligible: true, segment: 1, reason: null },
      { axis: 'speedTelemetry', eligible: true, segment: 1, reason: null },
      { axis: 'speedLegacy', eligible: true, segment: 1, reason: null },
      { axis: 'work', eligible: true, segment: 1, reason: null },
      { axis: 'cost', eligible: true, segment: 1, reason: null }
    ],
    ...overrides
  };
}

/**
 * Two battery runs of *Two initial suites* on 2026-10-08, newest first: #12 (runs 303 and 304, the
 * second matched to control run 404) and #11 (runs 301 and 302).
 */
export function ccBatteryRunRows(): CcBatteryRunRow[] {
  const twelve = ccBatteryRunRow(12, '2026-10-08T10:00:00Z', [303, 304]);
  twelve.members[1] = { ...twelve.members[1], matchedControlRunIds: [404] };
  return [twelve, ccBatteryRunRow(11, '2026-10-08T06:00:00Z', [301, 302])];
}

/** The member runs of {@link ccBatteryRunRows}, newest first, as the run table lists them. */
export function ccBatteryMemberRows(): CcRunRow[] {
  return ccBatteryRunRows().flatMap(row => [...row.members].reverse());
}

/** The sets of the battery fixtures: the battery, then Board Suite and Wiki Suite; the battery is the default. */
export function ccComparisonSets(overrides: Partial<CcComparisonSets> = {}): CcComparisonSets {
  return {
    sets: [
      {
        kind: 'battery', key: CC_BATTERY_SET_KEY, label: 'Two initial suites (revision 1)', unitCount: 2, memberRunCount: 4,
        latestStartedAtUtc: '2026-10-08T10:00:00Z'
      },
      {
        kind: 'suite', key: 'suite:id:5', label: 'Board Suite', unitCount: 8, memberRunCount: 8,
        latestStartedAtUtc: '2026-10-08T10:00:00Z'
      },
      {
        kind: 'suite', key: 'suite:id:6', label: 'Wiki Suite', unitCount: 2, memberRunCount: 2,
        latestStartedAtUtc: '2026-10-08T10:30:00Z'
      }
    ],
    defaultKey: CC_BATTERY_SET_KEY,
    ...overrides
  };
}

/** No set to compare within: the runs are listed and analyzed one by one, as before comparison sets. */
export function ccNoComparisonSets(): CcComparisonSets {
  return { sets: [], defaultKey: null };
}

/** A step-1 selection as the analysis request records it. */
export function ccRunSelectionView(overrides: Partial<CcRunSelectionView> = {}): CcRunSelectionView {
  return {
    recorded: true,
    rangeLabel: 'Last 30 days',
    rangeFromUtc: '2026-09-07T09:00:00.000Z',
    rangeToUtc: null,
    firstRunId: 102,
    lastRunId: null,
    leftOutRunIds: [104],
    unanalyzedRuns: [
      { runId: 104, period: 'comparison', startedAtUtc: '2026-09-20T08:00:00Z', reason: 'leftOut' },
      { runId: 105, period: 'comparison', startedAtUtc: '2026-09-26T08:00:00Z', reason: 'notSelected' }
    ],
    ...overrides
  };
}

// --- Composite events ---
//
// Runs 201–206 under harnesses 27, 28 and 29; runs 297–299 detected events but have no timeline
// point. The events group into four composites:
//   E1  2026-09-03, harness 27: System prompt (run 202), Knowledge base (runs 202 and 203).
//   E2  2026-09-05, harness 28: the bump 27 → 28 and Tool guides, both on run 204.
//   E3  2026-09-09, harness 29: Wiki on run 298 (no point, unknown harness), then the bump
//       28 → `29 (re-run 30)` on run 299 (no point), which gives the day's group its harness.
//   E4  2026-09-10, harness 29: Source code on run 206, then Corpus index on run 297 (no point,
//       unknown harness), which joins the day's earliest group.
// The dominant served model changes at run 204 (S1, 2026-09-05 08:00, the same instant as E2) and
// at run 205 (S2); run 206 reports none. Annotation A1 (2026-09-02) has a non-URL source, A2
// (2026-09-05 08:00, the same instant as E2 and S1) an `https:` one.

/** The timeline points of the composite-event fixture: runs 201–206, harness 27 → 28 → 29. */
export function ccEventPoints(): CcTimelinePoint[] {
  const served = (modelId: string) => [{ modelId, callCount: 40 }];
  return [
    ccPoint(201, '2026-09-01T08:00:00Z', { harnessVersion: '27', servedModelIds: served('gpt-5-2026-08') }),
    ccPoint(202, '2026-09-03T08:00:00Z', { harnessVersion: '27', servedModelIds: served('gpt-5-2026-08') }),
    ccPoint(203, '2026-09-03T14:00:00Z', { harnessVersion: '27', servedModelIds: served('gpt-5-2026-08') }),
    ccPoint(204, '2026-09-05T08:00:00Z', {
      harnessVersion: '28', servedModelIds: [{ modelId: 'gpt-5-2026-08', callCount: 5 }, { modelId: 'gpt-5-2026-09', callCount: 35 }]
    }),
    ccPoint(205, '2026-09-08T08:00:00Z', { harnessVersion: '28', servedModelIds: served('gpt-5-2026-08') }),
    ccPoint(206, '2026-09-10T08:00:00Z', { harnessVersion: '29', servedModelIds: [] })
  ];
}

/** The Overseer events of the composite-event fixture, deliberately out of time order. */
export function ccOverseerEvents(): CcEvent[] {
  const change = (runId: number, previousRunId: number, atUtc: string, kind: string, label: string, from: string, to: string) =>
    ccEvent({ runId, previousRunId, atUtc, kind, label, from, to });
  return [
    change(206, 205, '2026-09-10T08:00:00Z', 'SourceCodeHeadSha', 'source code updated on 2026-09-10', 'src1', 'src2'),
    change(203, 202, '2026-09-03T14:00:00Z', 'KnowledgeBaseHeadSha', 'knowledge base updated on 2026-09-03', 'kb2', 'kb3'),
    change(299, 298, '2026-09-09T10:00:00Z', 'HarnessVersion', 'harness 28 → 29 (re-run 30)', '28', '29 (re-run 30)'),
    change(204, 203, '2026-09-05T08:00:00Z', 'ToolGuidesSha256', 'tool guides edited on 2026-09-05', 'tg1', 'tg2'),
    change(202, 201, '2026-09-03T08:00:00Z', 'KnowledgeBaseHeadSha', 'knowledge base updated on 2026-09-03', 'kb1', 'kb2'),
    change(297, 206, '2026-09-10T12:00:00Z', 'CorpusIndexFingerprintsJson', 'corpus index rebuilt on 2026-09-10', 'ix1', 'ix2'),
    change(204, 203, '2026-09-05T08:00:00Z', 'HarnessVersion', 'harness 27 → 28', '27', '28'),
    change(298, 206, '2026-09-09T09:00:00Z', 'WikiHeadSha', 'wiki updated on 2026-09-09', 'wk1', 'wk2'),
    change(202, 201, '2026-09-03T08:00:00Z', 'CandidateSystemPromptSha256', 'system prompt edited on 2026-09-03', 'sp1', 'sp2')
  ];
}

/** The annotations of the composite-event fixture, later one first: A2 has an `https:` source, A1 a non-URL one. */
export function ccEventAnnotations(): CcAnnotation[] {
  return [
    ccAnnotation(12, {
      atUtc: '2026-09-05T08:00:00Z', kind: 'modelRelease', text: 'gpt-5-2026-09 snapshot released',
      sourceUrl: 'https://example.com/release-notes', createdAtUtc: '2026-09-05T09:00:00Z'
    }),
    ccAnnotation(11, {
      atUtc: '2026-09-02T12:00:00Z', kind: 'providerStatement', text: 'Provider reported elevated latency',
      sourceUrl: 'status page, 2 September', createdAtUtc: '2026-09-02T13:00:00Z'
    })
  ];
}

/** {@link ccTimeline} with the composite-event fixture's points, events and annotations. */
export function ccEventTimeline(overrides: Partial<CcTimeline> = {}): CcTimeline {
  return ccTimeline({
    points: ccEventPoints(),
    events: ccOverseerEvents(),
    annotations: ccEventAnnotations(),
    ...overrides
  });
}

const ENDPOINT_NAMES: Record<string, string> = {
  P1: 'Quality', P2: 'Time to first answer text', P3: 'Answer streaming rate', P4: 'Work per turn', P5: 'Cost per question'
};

export function ccEndpoint(id: string, overrides: Partial<CcEndpointResult> = {}): CcEndpointResult {
  return {
    id,
    name: ENDPOINT_NAMES[id] ?? id,
    unit: id === 'P1' ? 'index points' : 'log ratio',
    scale: id === 'P1' ? 'difference' : 'logRatio',
    margin: id === 'P1' ? 3 : 0.14,
    marginText: id === 'P1' ? '±3 index points' : '±15 %',
    direction: id === 'P4' ? 'work' : id === 'P1' || id === 'P3' ? 'higherIsBetter' : 'lowerIsBetter',
    computed: true,
    notComputedReason: null,
    estimate: 0.5,
    estimatePercent: id === 'P1' ? null : 2.1,
    ci95: { lower: -1, upper: 2 },
    ci90: { lower: -0.8, upper: 1.8 },
    ci95Percent: id === 'P1' ? null : { lower: -4, upper: 8 },
    pValue: 0.4,
    adjustedPValue: 0.8,
    pValueMethod: 'bootstrap',
    verdict: 'equivalent',
    verdictLabel: 'equivalent',
    grade: 'established',
    gradeReasons: [],
    minimumDetectableEffect: 1.8,
    minimumDetectableEffectPercent: id === 'P1' ? null : 9,
    minimumDetectableEffectNote: null,
    runsPerPeriodForMargin: null,
    minimumSampleMet: true,
    minimumSampleDetail: '3 runs on 3 days per period',
    legacyProxy: false,
    usesLegacyData: false,
    commonGrader: false,
    relaxedPooling: false,
    baselineRunCount: 3,
    comparisonRunCount: 3,
    baselineRunIds: [101, 102, 103],
    comparisonRunIds: [104, 105, 106],
    itemCount: 60,
    strataUsed: [],
    stratumExcludedShare: null,
    robustnessChecks: [],
    ...overrides
  };
}

export function ccProtocol(): CcProtocol {
  return {
    protocolVersion: 'V1', alpha: 0.05, secondaryFalseDiscoveryRate: 0.05, power: 0.8, bootstrapReplicates: 4000,
    bootstrapSeed: 1, minimumRunsPerPeriod: 2, minimumDaysPerPeriod: 2, minimumPairedItems: 20, minimumSpeedRunsPerStratum: 3,
    minimumRunsPerStratumForSignCheck: 2, ownWaitShareMaterialChange: 0.05, flipPassThreshold: 50, graderDriftMargin: 3,
    usBusinessHourStrata: [3, 4, 5], usBusinessHoursDefinition: 'US business hours are weekdays 14–22 UTC.',
    endpoints: [], overrides: [], isOverridden: false, label: 'V1'
  };
}

export function ccAttribution(side: string, label: string, overrides: Partial<CcAttributionResult> = {}): CcAttributionResult {
  return {
    label, side, grade: 'indicated', rule: 'R1', endpoints: ['P2'], eventRefs: [], evidence: `${label} evidence`, ...overrides
  };
}

export function ccAnalysisResult(overrides: Partial<CcAnalysisResult> = {}): CcAnalysisResult {
  return {
    analysisId: 7,
    createdAtUtc: '2026-10-02T09:00:00Z',
    name: 'September check',
    headline: 'Overseer chat with GPT-5 high: quality equivalent; speed slower; work equivalent; cost equivalent within weekdays 08–12 UTC',
    headlineReliabilityIncreases: [],
    subject: ccTimeline().subject,
    scope: {
      text: 'weekdays 08–12 UTC', strataIndexes: [2], strataUsed: ['weekday 08–12 UTC'], excludedShare: 0,
      oneTimeStratum: true, timeOfDayAssessable: false, usBusinessHoursCovered: false, outsideBusinessHoursCovered: true
    },
    baseline: {
      name: 'baseline', startUtc: '2026-09-01T00:00:00Z', endUtc: '2026-09-14T23:59:59.999Z', runIds: [101, 102, 103], runCount: 3,
      days: ['2026-09-01', '2026-09-05', '2026-09-12'], answerCount: 60, itemCount: 20, suiteNames: ['Board Suite'], legacyRunCount: 1
    },
    comparison: {
      name: 'comparison', startUtc: '2026-09-15T00:00:00Z', endUtc: '2026-10-01T23:59:59.999Z', runIds: [104, 105, 106], runCount: 3,
      days: ['2026-09-20', '2026-09-26', '2026-10-01'], answerCount: 60, itemCount: 20, suiteNames: ['Board Suite'], legacyRunCount: 0
    },
    protocol: ccProtocol(),
    protocolLabel: 'V1',
    endpoints: [
      ccEndpoint('P1'),
      ccEndpoint('P2', { verdict: 'changedDegraded', verdictLabel: 'degraded', grade: 'indicated', legacyProxy: true, estimatePercent: 18 }),
      ccEndpoint('P3'),
      ccEndpoint('P4', { verdictLabel: 'equivalent' }),
      ccEndpoint('P5')
    ],
    secondaryFamilies: [],
    robustnessChecks: [],
    reliability: [],
    events: [ccEvent()],
    boundaries: [],
    segments: [],
    controls: { matches: [], effects: [], missingControls: [], controlRunIds: [] },
    attribution: {
      totalChanges: [{ endpointId: 'P2', name: 'Time to first answer text', verdictLabel: 'degraded', grade: 'indicated' }],
      attributions: [
        ccAttribution('provider', 'Provider-side latency change'),
        ccAttribution('ours', 'Tool guides edit'),
        ccAttribution('undetermined', 'Unexplained work shift', { endpoints: ['P4'] })
      ]
    },
    servedModels: {
      baseline: [], comparison: [], changed: false, baselineCalls: 0, comparisonCalls: 0, baselineTierMismatchCalls: 0,
      comparisonTierMismatchCalls: 0, baselineFallbackCalls: 0, comparisonFallbackCalls: 0, baselineServedSpeeds: [],
      comparisonServedSpeeds: [], servedConfigurationDiffers: false
    },
    ownWaits: [],
    commonGrader: null,
    graderDrift: [],
    priceCard: ccTimeline().priceCard,
    annotations: [],
    dataQuality: [{ kind: 'legacy', text: 'One baseline run has no call telemetry.' }],
    limitations: ['Only one time stratum is common to both periods.'],
    nextRuns: [
      { kind: 'control', period: 'comparison', endpointId: 'P2', reason: 'No control run in the comparison period.',
        suggestion: 'Run Claude Opus on Board Suite.', repeatRunId: 205 },
      { kind: 'regrade', period: 'baseline', endpointId: 'P1', reason: 'Native grades only.',
        suggestion: 'Re-grade with a common assessor.', repeatRunId: null }
    ],
    inputSha256: 'a'.repeat(64),
    analysisCodeVersion: 1,
    ...overrides
  };
}

export function ccAnalysisSummary(id: number, overrides: Partial<CcAnalysisSummary> = {}): CcAnalysisSummary {
  return {
    id,
    name: `Analysis ${id}`,
    subjectModelKey: 'openai/gpt-5|high',
    baselineStartUtc: '2026-09-01T00:00:00Z',
    baselineEndUtc: '2026-09-14T23:59:59.999Z',
    comparisonStartUtc: '2026-09-15T00:00:00Z',
    comparisonEndUtc: '2026-10-01T23:59:59.999Z',
    protocolVersion: 'V1',
    relaxedPooling: false,
    commonGraderSnapshotId: null,
    headline: 'Overseer chat with GPT-5 high: quality equivalent',
    inputSha256: 'b'.repeat(64),
    analysisCodeVersion: 1,
    createdAtUtc: '2026-10-02T09:00:00Z',
    reportDocumentCount: 0,
    comparisonSetKey: null,
    comparisonSetLabel: null,
    ...overrides
  };
}

export function ccRegradeEstimate(overrides: Partial<CcRegradeEstimate> = {}): CcRegradeEstimate {
  return {
    assessorConfigId: 21,
    assessorDisplay: 'Claude Opus assessor',
    assessorRefusal: null,
    runs: [
      { runId: 101, eligible: true, refusal: null, gradableAnswerCount: 20, recordedAssessorInputTokens: 1000,
        recordedAssessorOutputTokens: 200, recordedAssessorCacheReadTokens: 0, recordedAssessorCacheCreationTokens: 0, estimatedCostUsd: 0.4 },
      { runId: 103, eligible: false, refusal: 'The run has no gradable answers.', gradableAnswerCount: 0, recordedAssessorInputTokens: 0,
        recordedAssessorOutputTokens: 0, recordedAssessorCacheReadTokens: 0, recordedAssessorCacheCreationTokens: 0, estimatedCostUsd: null }
    ],
    eligibleRunCount: 1,
    estimatedTotalCostUsd: 0.4,
    pricingAvailable: true,
    note: 'From the recorded assessor tokens.',
    ...overrides
  };
}

export function ccRegradeJob(overrides: Partial<CcRegradeJob> = {}): CcRegradeJob {
  return {
    id: 'job-1', status: 'running', assessorConfigId: 21, assessorDisplay: 'Claude Opus assessor', runIds: [101],
    total: 1, done: 0, currentRunId: 101, errors: [], startedAtUtc: '2026-10-02T10:00:00Z', completedAtUtc: null,
    startedByUserName: 'admin', ...overrides
  };
}

export function ccReportEstimate(overrides: Partial<CcReportEstimate> = {}): CcReportEstimate {
  return {
    estimates: [],
    estimatedTotalCostUsd: 0.12,
    refusal: null,
    sameProviderWarning: null,
    providerIssueReportAvailable: false,
    providerIssueReportReason: 'No change attributed to the provider is established or indicated.',
    ...overrides
  };
}

/** {@link ccTimeline}'s subject with the display name as the server builds it: the thinking level in parentheses. */
export function ccSubjectWithLevel(overrides: Partial<CcSubject> = {}): CcSubject {
  return { ...ccTimeline().subject, displayName: 'GPT-5 (high)', ...overrides };
}

/** One analyzed unit of a stored result: a run unit standing for itself unless `overrides` says otherwise. */
export function ccUnitView(unitId: number, period: string, overrides: Partial<CcUnitView> = {}): CcUnitView {
  return {
    unitId,
    kind: 'run',
    period,
    startedAtUtc: '2026-09-01T08:00:00Z',
    memberRunIds: [unitId],
    ...overrides
  };
}

/**
 * The report-writing job of analysis 7 with the writer *Claude writer* (configuration 30), writing: the
 * Executive Summary written (document 501), the Report for AI Researchers and Developers being written
 * and the Internal Brief pending, with a two-line log. `jobOverrides` apply to the wrapped pack job.
 */
export function ccReportJob(
  overrides: Partial<BenchmarkRunReportJobDto> = {},
  jobOverrides: Partial<BenchmarkReportPackJobDto> = {}
): BenchmarkRunReportJobDto {
  const job: BenchmarkReportPackJobDto = {
    id: 'cc-job-1',
    packId: 'cc-pack-1',
    subjectKey: 'chat-consistency:7',
    subjectLabel: 'September check',
    suiteId: null,
    suiteName: '',
    writerConfigId: 30,
    writerDisplayName: 'Claude writer',
    startedByUserId: null,
    startedAtUtc: '2026-10-02T10:00:05Z',
    completedAtUtc: null,
    status: 'Running',
    totalModelCalls: 3,
    inputTokens: 42_000,
    outputTokens: 9_000,
    costUsd: 0.21,
    documents: [
      {
        audience: BenchmarkReportAudience.ExecutiveSummary, status: 'Completed', documentId: 501, errorMessage: null, modelCalls: 2,
        startedAtUtc: '2026-10-02T10:00:05Z', completedAtUtc: '2026-10-02T10:01:05Z', inputTokens: 30_000, outputTokens: 6_000, costUsd: 0.15
      },
      {
        audience: BenchmarkReportAudience.TechnicalReport, status: 'Writing', documentId: null, errorMessage: null, modelCalls: 1,
        startedAtUtc: '2026-10-02T10:01:05Z', completedAtUtc: null, inputTokens: 12_000, outputTokens: 3_000, costUsd: 0.06
      },
      {
        audience: BenchmarkReportAudience.InternalBrief, status: 'Pending', documentId: null, errorMessage: null, modelCalls: 0,
        startedAtUtc: null, completedAtUtc: null, inputTokens: 0, outputTokens: 0, costUsd: null
      }
    ],
    log: [
      { timestampUtc: '2026-10-02T10:00:05Z', message: 'Writing Executive Summary.', severity: 'Info' },
      { timestampUtc: '2026-10-02T10:01:05Z', message: 'Executive Summary written.', severity: 'Info' }
    ],
    serverTimeUtc: '2026-10-02T10:01:30Z',
    ...jobOverrides
  };
  return {
    runId: 7,
    status: BenchmarkRunReportDocumentsStatus.Writing,
    message: null,
    phase: 'Writing',
    queuedAtUtc: '2026-10-02T10:00:00Z',
    slotAcquiredAtUtc: '2026-10-02T10:00:05Z',
    finishedAtUtc: null,
    cancelRequestedAtUtc: null,
    jobsAhead: null,
    blockingJobLabel: null,
    audiences: [BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief],
    writerConfigId: 30,
    writerDisplayName: 'Claude writer',
    writerProvider: 'Anthropic',
    writerModelId: 'claude-30',
    writerThinkingLevel: null,
    job,
    serverTimeUtc: '2026-10-02T10:01:30Z',
    ...overrides
  };
}

/** A benchmark-capable system configuration, as the workspace store lists them. */
export function ccConfig(id: number, overrides: Partial<SystemAiConfigDto> = {}): SystemAiConfigDto {
  return {
    id,
    displayName: `Config ${id}`,
    provider: 'Anthropic',
    modelId: `claude-${id}`,
    isEnabled: true,
    hasApiKey: true,
    modelRole: 4,
    ...overrides
  } as SystemAiConfigDto;
}

/** Text content with its whitespace collapsed. */
export function textOf(el: Element | null | undefined): string {
  return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
}
