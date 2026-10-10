/**
 * The GnollBench chat consistency API's records, as `AdminChatConsistencyController` returns them:
 * camelCase JSON with enums as camelCase strings (`ChatConsistencyJson.Options`). A double may arrive
 * as the string `NaN`, `Infinity` or `-Infinity`, so every floating-point field is a {@link CcNumber}
 * and is read through `ccNumber()` (`chat-consistency-format.ts`).
 *
 * The report endpoints reuse the run report-writing shapes of `admin-benchmark.service.ts`, with the
 * Provider Issue Report added to the audiences.
 */

import {
  BenchmarkReportAudience,
  BenchmarkRunReportEstimateDto,
  WriteRunReportDocumentsRequest
} from '../../../services/admin-benchmark.service';

/** A JSON number, or a named floating-point literal (`NaN`, `Infinity`, `-Infinity`). */
export type CcNumber = number | string;

// --- Enums, as the server's camelCase names ---

export type CcAxis = 'quality' | 'speedTelemetry' | 'speedLegacy' | 'work' | 'cost';
export type CcVerdict = 'changedDegraded' | 'changedImproved' | 'changedNegligible' | 'equivalent' | 'inconclusive';
export type CcGrade = 'established' | 'indicated' | 'notEstablished';
export type CcCheckStatus = 'passed' | 'failed' | 'notAssessable';
export type CcEffectScale = 'difference' | 'logRatio';
export type CcMeasurementChangeKind =
  'grading' | 'scoring' | 'candidateTiming' | 'callTelemetry' | 'candidateAccounting' | 'pricing';
export type CcAnnotationKind =
  'modelRelease' | 'providerStatement' | 'providerConfirmedCause' | 'priceChange' | 'ourChange' | 'other';
export type CcRunStatus =
  'running' | 'completed' | 'completedWithErrors' | 'failed' | 'canceled' | 'completedWithLimits';
/** A battery run's `BenchmarkRunSeriesStatus`; `cancelled` is the server's spelling on the wire. */
export type CcBatteryRunStatus =
  'pending' | 'running' | 'waitingForCap' | 'stopped' | 'completed' | 'completedWithErrors' | 'cancelled' | 'failed';

/** What a comparison set holds: one battery definition, or one suite. */
export type CcComparisonSetKind = 'battery' | 'suite';
/** What an analysis counts: a run in a suite set or a run-by-run analysis, a battery run in a battery set. */
export type CcUnitKind = 'run' | 'batteryRun';

/** The key prefixes of the comparison sets: `battery:<DefinitionSha256>`, `suite:<suite identity>`. */
export const CC_BATTERY_SET_PREFIX = 'battery:';
export const CC_SUITE_SET_PREFIX = 'suite:';

/** The primary endpoints, in protocol order. */
export const CC_ENDPOINT_IDS = ['P1', 'P2', 'P3', 'P4', 'P5'] as const;
export type CcEndpointId = typeof CC_ENDPOINT_IDS[number];

/** The axes a run's eligibility is given for, in the run table's column order. */
export const CC_AXES: readonly CcAxis[] = ['quality', 'speedTelemetry', 'speedLegacy', 'work', 'cost'];

/** Every annotation kind with its plain-language label, in the add form's order. */
export const CC_ANNOTATION_KINDS: readonly { readonly kind: CcAnnotationKind; readonly label: string }[] = [
  { kind: 'modelRelease', label: 'Model release' },
  { kind: 'providerStatement', label: 'Provider statement' },
  { kind: 'providerConfirmedCause', label: 'Provider confirmed a cause' },
  { kind: 'priceChange', label: 'Price change' },
  { kind: 'ourChange', label: 'Change on our side' },
  { kind: 'other', label: 'Other' }
];

// --- Model axes, timeline and run table ---

export interface CcModelAxis {
  key: string;
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
  serviceTier: string | null;
  runCount: number;
  telemetryRunCount: number;
  firstRunAtUtc: string;
  lastRunAtUtc: string;
  latestRunId: number;
  suiteNames: string[];
  /** Distinct battery runs with a member on this axis. */
  batteryRunCount: number;
}

export interface CcSubject {
  key: string;
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
  serviceTier: string | null;
  configurationId: number | null;
}

export interface CcServedModelCount {
  modelId: string;
  callCount: number;
}

export interface CcCommonGraderPoint {
  snapshotId: number;
  display: string;
  calibrationId: number;
  calibratedAtUtc: string;
  meanQuality: CcNumber;
  itemCount: number;
}

export interface CcTimelinePoint {
  runId: number;
  startedAtUtc: string;
  suiteName: string;
  suiteId: number | null;
  harnessVersion: string | null;
  status: CcRunStatus;
  isLegacy: boolean;
  isAnchor: boolean;
  qualityIndex: number | null;
  nativeMeanQuality: CcNumber | null;
  commonGraderQuality: CcCommonGraderPoint[];
  medianTimeToFirstAnswerTextMs: CcNumber | null;
  medianStreamingRate: CcNumber | null;
  streamingRateEstimated: boolean;
  medianModelTimeMs: CcNumber | null;
  /** `telemetry` or `legacy proxy`. */
  latencyLabel: string;
  outputTokensPerAnswer: CcNumber | null;
  toolCallsPerAnswer: CcNumber | null;
  costPerQuestionUsd: CcNumber | null;
  terminalFailureRate: CcNumber | null;
  timeoutRate: CcNumber | null;
  emptyAnswerRate: CcNumber | null;
  refusalRate: CcNumber | null;
  toolBudgetExhaustedRate: CcNumber | null;
  servedModelIds: CcServedModelCount[];
  strata: string[];
  strataEstimated: boolean;
  answerCount: number;
  maxParallelQuestions: number;
}

/**
 * One battery run of the subject on the timeline: its usable members pooled into one point. `runId`
 * is the battery run id; the measures are over the union of the members' answers, and the quality is
 * `overallIndex`, the battery analysis's Overall Index (`qualityIndex` is always null).
 */
export interface CcBatteryTimelinePoint extends CcTimelinePoint {
  /** `battery:` plus the definition hash. */
  setKey: string;
  batteryName: string;
  definitionRevision: number | null;
  completedAtUtc: string | null;
  batteryStatus: CcBatteryRunStatus;
  suiteCount: number;
  /** Every suite slot holds a usable member. */
  complete: boolean;
  /** For example `1 of 2 suites usable`; null when complete. */
  incompleteReason: string | null;
  /** The usable members on the subject's axis, in suite order. */
  memberRunIds: number[];
  /** The latest current battery analysis's Overall Index; null with `overallIndexNote` saying why. */
  overallIndex: CcNumber | null;
  overallIndexNote: string | null;
  /** The 95 % half-width of `overallIndex`; null or absent when there is none. */
  overallIndexHalfWidth?: CcNumber | null;
  /** `question sampling only` when the half-width rests on one round; else null. */
  overallIndexIntervalNote?: string | null;
}

export interface CcEvent {
  atUtc: string;
  kind: string;
  /** For example "tool guides edited on 2026-10-03". */
  label: string;
  from: string | null;
  to: string | null;
  runId: number;
  previousRunId: number;
  subjectKey: string;
  inTargetSeries: boolean;
}

export interface CcAnnotation {
  id: number;
  atUtc: string;
  provider: string | null;
  modelId: string | null;
  kind: CcAnnotationKind;
  text: string;
  sourceUrl: string | null;
  createdAtUtc: string;
}

export interface CcPriceCard {
  available: boolean;
  source: string;
  runId: number | null;
  inputPerMillion: number | null;
  outputPerMillion: number | null;
  cachedInputPerMillion: number | null;
  cacheWritePerMillion: number | null;
  asOf: string | null;
}

export interface CcTimeline {
  subject: CcSubject;
  fromUtc: string | null;
  toUtc: string | null;
  points: CcTimelinePoint[];
  /** Every battery run in the dates with a member on the subject's axis, of every battery; by start, then id. */
  batteryPoints: CcBatteryTimelinePoint[];
  events: CcEvent[];
  annotations: CcAnnotation[];
  priceCard: CcPriceCard;
}

export interface CcAxisEligibility {
  axis: CcAxis;
  eligible: boolean;
  segment: number | null;
  reason: string | null;
}

export interface CcRegradeCoverage {
  snapshotId: number;
  display: string;
  calibrationIds: number[];
  latestAtUtc: string;
}

export interface CcRunRow {
  runId: number;
  startedAtUtc: string;
  suiteName: string;
  harnessVersion: string | null;
  scoringMethodVersion: number;
  status: CcRunStatus;
  isLegacy: boolean;
  isAnchor: boolean;
  eligibility: CcAxisEligibility[];
  regradeCoverage: CcRegradeCoverage[];
  matchedControlRunIds: number[];
  servedModelIds: CcServedModelCount[];
  suiteId: number | null;
  /** The run's suite identity (`id:5`, or `name:…` without a recorded id); its suite set's key is `suite:` plus this. */
  suiteKey: string;
  /** The newest battery run holding this run as a member; null when none does. */
  batteryRunId: number | null;
  batteryName: string | null;
  /** The 1-based position of the run's suite in that battery run. */
  batterySuitePosition: number | null;
  batterySuiteCount: number | null;
}

/** A battery or suite the subject can be compared within, over the step-1 dates. */
export interface CcComparisonSet {
  kind: CcComparisonSetKind;
  /** `battery:<DefinitionSha256>` or `suite:<suite identity>`. */
  key: string;
  /** The battery name with its revisions (`Two initial suites (revision 1)`), or the suite name. */
  label: string;
  /** Battery runs of a battery set, runs of a suite set. */
  unitCount: number;
  memberRunCount: number;
  latestStartedAtUtc: string;
}

/** The sets the subject can be compared within: batteries first, then suites, each group newest first. */
export interface CcComparisonSets {
  sets: CcComparisonSet[];
  /** The set step 1 selects by default; null when there is no run. */
  defaultKey: string | null;
}

/** A comparison set as a request names it. */
export interface CcComparisonSetRef {
  kind: CcComparisonSetKind;
  key: string;
}

/** One battery run of the subject, with its usable members. */
export interface CcBatteryRunRow {
  batteryRunId: number;
  batteryId: number | null;
  batteryName: string;
  definitionSha256: string;
  definitionRevision: number;
  /** `battery:` plus the definition hash. */
  setKey: string;
  startedAtUtc: string;
  completedAtUtc: string | null;
  status: CcBatteryRunStatus;
  suiteCount: number;
  /** Every suite slot holds a usable member; only a complete battery run is analyzed. */
  complete: boolean;
  /** For example `1 of 2 suites usable`; null when complete. */
  incompleteReason: string | null;
  /** The members' harness versions, distinct, ascending. */
  harnessVersions: string[];
  /** The usable members on the subject's axis, in suite order. */
  members: CcRunRow[];
  /** Per axis: eligible when every member is, else the members' reasons, each prefixed `#<run id>: `. */
  eligibility: CcAxisEligibility[];
}

export interface CcEndpointProtocol {
  id: string;
  name: string;
  unit: string;
  scale: CcEffectScale;
  margin: CcNumber;
  higherIsBetter: boolean;
  workDirection: boolean;
  axis: CcAxis;
  runClustersOnly: boolean;
  stratified: boolean;
  pairing: string;
  marginText: string;
}

export interface CcProtocolOverride {
  field: string;
  from: string;
  to: string;
}

export interface CcProtocol {
  protocolVersion: string;
  alpha: CcNumber;
  secondaryFalseDiscoveryRate: CcNumber;
  power: CcNumber;
  bootstrapReplicates: number;
  bootstrapSeed: number;
  minimumRunsPerPeriod: number;
  minimumDaysPerPeriod: number;
  minimumPairedItems: number;
  minimumSpeedRunsPerStratum: number;
  minimumRunsPerStratumForSignCheck: number;
  ownWaitShareMaterialChange: CcNumber;
  flipPassThreshold: number;
  graderDriftMargin: CcNumber;
  usBusinessHourStrata: number[];
  usBusinessHoursDefinition: string;
  endpoints: CcEndpointProtocol[];
  overrides: CcProtocolOverride[];
  isOverridden: boolean;
  label: string;
}

/**
 * The overrides a request may apply. Margins are keyed by endpoint id: index points for a difference
 * endpoint, a fraction (0.15 = ±15 %) for a log-ratio one.
 */
export interface CcProtocolOverrides {
  margins?: Record<string, number>;
  alpha?: number;
  bootstrapReplicates?: number;
  bootstrapSeed?: number;
  minimumPairedItems?: number;
  minimumRunsPerPeriod?: number;
  minimumDaysPerPeriod?: number;
  minimumSpeedRunsPerStratum?: number;
}

// --- Analysis request and result ---

export interface CcAnalysisRequest {
  name?: string | null;
  subjectModelKey: string;
  baselineStartUtc: string;
  baselineEndUtc: string;
  comparisonStartUtc: string;
  comparisonEndUtc: string;
  baselineRunIds?: number[] | null;
  comparisonRunIds?: number[] | null;
  controlRunIds?: number[] | null;
  protocolOverrides?: CcProtocolOverrides | null;
  relaxedPooling?: boolean;
  commonGraderSnapshotId?: number | null;
  availableOtherProviderModels?: string[] | null;
  /** How the runs were chosen in step 1; recorded with the analysis, never used to pick runs. */
  runSelection?: CcRunSelection | null;
  /** The battery or suite compared within; absent analyzes the runs one by one. */
  comparisonSet?: CcComparisonSetRef | null;
  /** With a battery set, the battery runs of each period; the run ids are then not sent. */
  baselineBatteryRunIds?: number[] | null;
  comparisonBatteryRunIds?: number[] | null;
}

/** The step-1 run selection as the request carries it. */
export interface CcRunSelection {
  /** The step-1 dates as shown, for example `Last 30 days`; at most 64 characters. */
  rangeLabel: string | null;
  rangeFromUtc: string | null;
  rangeToUtc: string | null;
  firstRunId: number | null;
  lastRunId: number | null;
  /** Sorted ascending; at most 5,000. */
  leftOutRunIds: number[];
  /** In a battery set, the first and last battery runs and the battery runs left out (ascending, at most 5,000). */
  firstBatteryRunId?: number | null;
  lastBatteryRunId?: number | null;
  leftOutBatteryRunIds?: number[];
}

/** Why a usable run of the subject inside a period was not analyzed. */
export type CcUnanalyzedReason =
  'leftOut' | 'outsideDateRange' | 'beforeFirstRun' | 'afterLastRun' | 'notSelected' | 'outsideComparisonSet';

export interface CcUnanalyzedRun {
  runId: number;
  /** `baseline` or `comparison`. */
  period: string;
  startedAtUtc: string;
  reason: CcUnanalyzedReason;
  /** In a battery set, the battery run the reason applies to. */
  batteryRunId?: number | null;
}

/** The run selection as recorded with the analysis. */
export interface CcRunSelectionView {
  /** The request carried a selection; false for analyses saved before it was recorded. */
  recorded: boolean;
  rangeLabel: string | null;
  rangeFromUtc: string | null;
  rangeToUtc: string | null;
  firstRunId: number | null;
  lastRunId: number | null;
  leftOutRunIds: number[];
  firstBatteryRunId?: number | null;
  lastBatteryRunId?: number | null;
  leftOutBatteryRunIds?: number[];
  /** Ordered by start, then id. */
  unanalyzedRuns: CcUnanalyzedRun[];
}

/** The battery or suite an analysis compared within, as recorded with the result. */
export interface CcComparedSet {
  kind: CcComparisonSetKind;
  key: string;
  label: string;
}

/** One analyzed unit: a battery run in a battery set, a run otherwise. */
export interface CcUnitView {
  unitId: number;
  kind: CcUnitKind;
  /** `baseline` or `comparison`. */
  period: string;
  startedAtUtc: string;
  /** The runs merged into the unit, in suite order; the run itself for a run unit. */
  memberRunIds: number[];
}

export interface CcPeriodSummary {
  /** `baseline` or `comparison`. */
  name: string;
  startUtc: string;
  endUtc: string;
  runIds: number[];
  runCount: number;
  days: string[];
  answerCount: number;
  itemCount: number;
  suiteNames: string[];
  legacyRunCount: number;
}

export interface CcScope {
  text: string;
  strataIndexes: number[];
  strataUsed: string[];
  excludedShare: CcNumber;
  oneTimeStratum: boolean;
  timeOfDayAssessable: boolean;
  usBusinessHoursCovered: boolean;
  outsideBusinessHoursCovered: boolean;
}

export interface CcInterval {
  lower: CcNumber;
  upper: CcNumber;
}

export interface CcCheck {
  endpointId: string;
  name: string;
  status: CcCheckStatus;
  detail: string;
}

export interface CcEndpointResult {
  id: string;
  name: string;
  unit: string;
  scale: CcEffectScale;
  margin: CcNumber;
  marginText: string;
  /** `higherIsBetter`, `lowerIsBetter` or `work`. */
  direction: string;
  computed: boolean;
  notComputedReason: string | null;
  /** Why the endpoint was not computed; null when computed, absent before analysis code version 6. */
  notComputedKind?: CcNotComputedKind | null;
  estimate: CcNumber | null;
  estimatePercent: CcNumber | null;
  ci95: CcInterval | null;
  ci90: CcInterval | null;
  ci95Percent: CcInterval | null;
  pValue: CcNumber | null;
  adjustedPValue: CcNumber | null;
  pValueMethod: string;
  verdict: CcVerdict | null;
  verdictLabel: string;
  grade: CcGrade;
  gradeReasons: string[];
  minimumDetectableEffect: CcNumber | null;
  minimumDetectableEffectPercent: CcNumber | null;
  minimumDetectableEffectNote: string | null;
  runsPerPeriodForMargin: number | null;
  minimumSampleMet: boolean;
  minimumSampleDetail: string;
  legacyProxy: boolean;
  usesLegacyData: boolean;
  commonGrader: boolean;
  relaxedPooling: boolean;
  baselineRunCount: number;
  comparisonRunCount: number;
  baselineRunIds: number[];
  comparisonRunIds: number[];
  itemCount: number;
  strataUsed: string[];
  stratumExcludedShare: CcNumber | null;
  robustnessChecks: CcCheck[];
}

export interface CcSecondaryResult {
  id: string;
  name: string;
  unit: string;
  baselineValue: CcNumber | null;
  comparisonValue: CcNumber | null;
  estimate: CcNumber | null;
  ci95: CcInterval | null;
  pValue: CcNumber | null;
  adjustedPValue: CcNumber | null;
  rejected: boolean;
  method: string;
  itemCount: number | null;
  verdict: CcVerdict | null;
  note: string | null;
}

export interface CcFamilyResult {
  id: string;
  name: string;
  results: CcSecondaryResult[];
  note: string | null;
}

export interface CcRateResult {
  id: string;
  name: string;
  denominator: string;
  baselineCount: number;
  baselineTotal: number;
  baselineRate: CcNumber | null;
  baselineCi95: CcInterval | null;
  comparisonCount: number;
  comparisonTotal: number;
  comparisonRate: CcNumber | null;
  comparisonCi95: CcInterval | null;
  pValue: CcNumber | null;
  adjustedPValue: CcNumber | null;
  increased: boolean;
  establishedIncrease: boolean;
}

export interface CcBoundary {
  subjectKey: string;
  fromRunId: number;
  toRunId: number;
  atUtc: string;
  kind: CcMeasurementChangeKind;
  axes: CcAxis[];
  bridged: boolean;
  reason: string;
}

export interface CcSegmentView {
  runId: number;
  startedAtUtc: string;
  /** `baseline`, `comparison` or `control`. */
  role: string;
  quality: number | null;
  speedTelemetry: number | null;
  speedLegacy: number | null;
  work: number | null;
  cost: number | null;
}

export interface CcControlMatch {
  period: string;
  targetRunId: number;
  controlRunId: number;
  controlSubjectKey: string;
  pairedItemCount: number;
}

export interface CcMissingControl {
  period: string;
  suiteName: string;
  fingerprint: string;
  suggestedText: string;
  targetRunId: number;
  /** In a battery comparison, the battery run the note is about. */
  batteryRunId?: number | null;
  /** The target's Overseer build no longer runs, so no control run can be made under it. */
  buildReplaced?: boolean;
}

export interface CcControlEffect {
  endpointId: string;
  controlSubjectKey: string;
  controlDisplay: string;
  controlProvider: string;
  sameProvider: boolean;
  controlBaselineRunIds: number[];
  controlComparisonRunIds: number[];
  itemCount: number;
  controlChange: CcNumber | null;
  controlChangeCi95: CcInterval | null;
  didEstimate: CcNumber | null;
  didCi95: CcInterval | null;
  didPValue: CcNumber | null;
  didIncludesZero: boolean;
  didSeparatesTarget: boolean;
  controlMovedSameWay: boolean;
}

export interface CcControls {
  matches: CcControlMatch[];
  effects: CcControlEffect[];
  missingControls: CcMissingControl[];
  controlRunIds: number[];
}

export interface CcTotalChange {
  endpointId: string;
  name: string;
  verdictLabel: string;
  grade: CcGrade;
}

/** `ours`, `provider`, `infrastructure` or `undetermined`. */
export type CcAttributionSide = 'ours' | 'provider' | 'infrastructure' | 'undetermined';

export interface CcAttributionResult {
  label: string;
  side: CcAttributionSide | string;
  grade: CcGrade;
  rule: string;
  endpoints: string[];
  eventRefs: string[];
  evidence: string;
}

export interface CcAttributionOutcome {
  totalChanges: CcTotalChange[];
  attributions: CcAttributionResult[];
}

export interface CcServedModels {
  baseline: CcServedModelCount[];
  comparison: CcServedModelCount[];
  changed: boolean;
  baselineCalls: number;
  comparisonCalls: number;
  baselineTierMismatchCalls: number;
  comparisonTierMismatchCalls: number;
  baselineFallbackCalls: number;
  comparisonFallbackCalls: number;
  baselineServedSpeeds: string[];
  comparisonServedSpeeds: string[];
  servedConfigurationDiffers: boolean;
}

export interface CcOwnWaits {
  period: string;
  permitWaitMs: number;
  backoffWaitMs: number;
  modelTimeMs: number;
  ownWaitShare: CcNumber | null;
  retryAttemptCount: number;
  answersWithTelemetry: number;
}

export interface CcCommonGrader {
  snapshotId: number;
  display: string;
  requested: boolean;
  calibrationIds: number[];
  coveredRunIds: number[];
  uncoveredControlRunIds: number[];
}

export interface CcGraderDrift {
  anchorRunId: number;
  snapshotId: number;
  display: string;
  earliestAtUtc: string;
  latestAtUtc: string;
  drift: CcNumber;
  itemCount: number;
  withinMargin: boolean;
}

export interface CcNote {
  kind: string;
  text: string;
}

export interface CcNextRun {
  /** `checkpoint`, `control`, `stratum` or `regrade`. */
  kind: string;
  period: string;
  endpointId: string | null;
  reason: string;
  suggestion: string;
  /** The run whose setup to repeat; null for a re-grade. */
  repeatRunId: number | null;
}

export interface CcAnalysisResult {
  analysisId: number | null;
  createdAtUtc: string | null;
  name: string;
  headline: string;
  headlineReliabilityIncreases: string[];
  subject: CcSubject;
  scope: CcScope;
  baseline: CcPeriodSummary;
  comparison: CcPeriodSummary;
  protocol: CcProtocol;
  protocolLabel: string;
  endpoints: CcEndpointResult[];
  secondaryFamilies: CcFamilyResult[];
  robustnessChecks: CcCheck[];
  reliability: CcRateResult[];
  events: CcEvent[];
  boundaries: CcBoundary[];
  segments: CcSegmentView[];
  controls: CcControls;
  attribution: CcAttributionOutcome;
  servedModels: CcServedModels;
  ownWaits: CcOwnWaits[];
  commonGrader: CcCommonGrader | null;
  graderDrift: CcGraderDrift[];
  priceCard: CcPriceCard;
  annotations: CcAnnotation[];
  dataQuality: CcNote[];
  limitations: string[];
  nextRuns: CcNextRun[];
  /** Absent in analyses saved before the selection was recorded; read it as not recorded. */
  runSelection?: CcRunSelectionView | null;
  /** The battery or suite compared within; null or absent for a run-by-run analysis and code version 3 or earlier. */
  comparisonSet?: CcComparedSet | null;
  /** What the minimum sample, the bootstrap and leave-one-out count; absent reads as `run`. */
  unitKind?: CcUnitKind;
  /** The analyzed units of both periods; absent in older analyses. */
  units?: CcUnitView[];
  /** The hours each period's answers started in, baseline first; null or absent before analysis code version 6. */
  periodHours?: CcPeriodHours[] | null;
  /** Each period's descriptive levels, baseline first; null or absent before analysis code version 6. */
  periodLevels?: CcPeriodLevels[] | null;
  /** The request as analyzed; null or absent before analysis code version 6. */
  request?: CcAnalysisRequest | null;
  inputSha256: string;
  analysisCodeVersion: number;
}

/** Why an endpoint was not computed. */
export type CcNotComputedKind =
  'measurementChanged' | 'noCommonStratum' | 'noTelemetry' | 'noPricing' | 'tooFewPairs' | 'other';

/** The hours one period's analyzed answers started in. */
export interface CcPeriodHours {
  period: 'baseline' | 'comparison';
  /** The time-of-week strata, ascending. */
  strata: string[];
  /** For example `weekdays 04–08 UTC`; `no timed answers` when there is none. */
  text: string;
}

/** Descriptive levels of one period over its analyzed units; not a comparison. */
export interface CcPeriodLevels {
  /** `baseline` or `comparison`. */
  period: string;
  answerCount: number;
  nativeMeanQuality: CcNumber | null;
  overallIndex: CcNumber | null;
  overallIndexHalfWidth: CcNumber | null;
  overallIndexIntervalNote: string | null;
  medianTimeToFirstAnswerTextMs: CcNumber | null;
  medianStreamingRate: CcNumber | null;
  /** The mean output tokens per delivered answer. */
  meanOutputTokensPerAnswer: CcNumber | null;
  meanCostPerQuestionUsd: CcNumber | null;
  failedAnswerCount: number;
}

/** Whether a saved analysis is out of date, and why (`GET analyses/{id}/freshness`). */
export interface CcAnalysisFreshness {
  analysisId: number;
  analysisCodeVersion: number;
  currentAnalysisCodeVersion: number;
  /** Saved under an earlier analysis code version than the running Overseer's. */
  earlierAnalysisCode: boolean;
  /** The inputs recomputed today differ from the stored ones; null when not checked. */
  inputsChanged: boolean | null;
  /** Why `inputsChanged` is null; null when it was checked. */
  inputsNote: string | null;
  outOfDate: boolean;
}

/** A saved analysis's model as its summary names it. */
export interface CcSummarySubject {
  displayName: string;
  provider: string;
  modelId: string;
  thinkingLevel: string | null;
  serviceTier: string | null;
}

/** One primary endpoint's verdict as a saved analysis's summary carries it. */
export interface CcEndpointBrief {
  id: string;
  name: string;
  computed: boolean;
  /** The server's verdict label: `degraded`, `more work`, `changed, negligible`, `inconclusive`, `not computable`… */
  verdictLabel: string;
  grade: CcGrade;
}

/**
 * The analysis code version the server analyzes under now (`ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion`).
 * A saved analysis below it was analyzed by earlier code.
 */
export const CC_CURRENT_ANALYSIS_CODE_VERSION = 6;

export interface CcAnalysisSummary {
  id: number;
  name: string;
  subjectModelKey: string;
  /** Null when the stored result does not name its model; absent from an older server. */
  subject?: CcSummarySubject | null;
  /** The primary endpoints in protocol order; empty or absent when the stored result lacks them. */
  endpoints?: CcEndpointBrief[];
  baselineStartUtc: string;
  baselineEndUtc: string;
  comparisonStartUtc: string;
  comparisonEndUtc: string;
  protocolVersion: string;
  relaxedPooling: boolean;
  commonGraderSnapshotId: number | null;
  headline: string | null;
  inputSha256: string;
  analysisCodeVersion: number;
  createdAtUtc: string;
  reportDocumentCount: number;
  /** The compared set; null for a run-by-run analysis and code version 3 or earlier. */
  comparisonSetKey: string | null;
  comparisonSetLabel: string | null;
}

// --- Re-grade ---

export interface CcRegradeRunEstimate {
  runId: number;
  eligible: boolean;
  refusal: string | null;
  gradableAnswerCount: number;
  recordedAssessorInputTokens: number;
  recordedAssessorOutputTokens: number;
  recordedAssessorCacheReadTokens: number;
  recordedAssessorCacheCreationTokens: number;
  estimatedCostUsd: number | null;
}

export interface CcRegradeEstimate {
  assessorConfigId: number;
  assessorDisplay: string;
  assessorRefusal: string | null;
  runs: CcRegradeRunEstimate[];
  eligibleRunCount: number;
  estimatedTotalCostUsd: number | null;
  pricingAvailable: boolean;
  note: string;
}

export interface CcRegradeRunError {
  runId: number;
  message: string;
}

/** `running`, `completed`, `completedWithErrors`, `canceled` or `failed`. */
export type CcRegradeJobStatus = 'running' | 'completed' | 'completedWithErrors' | 'canceled' | 'failed';

export interface CcRegradeJob {
  id: string;
  status: CcRegradeJobStatus | string;
  assessorConfigId: number;
  assessorDisplay: string;
  runIds: number[];
  total: number;
  done: number;
  currentRunId: number | null;
  errors: CcRegradeRunError[];
  startedAtUtc: string;
  completedAtUtc: string | null;
  startedByUserName: string | null;
}

// --- Anchors and annotations ---

export interface CcAnchorResponse {
  runId: number;
  isAnchor: boolean;
}

export interface CcAnnotationRequest {
  atUtc: string;
  provider: string | null;
  modelId: string | null;
  kind: CcAnnotationKind;
  text: string;
  sourceUrl: string | null;
}

// --- Reports ---

/** The four documents a chat consistency analysis is written as, with their labels, in the Write step's order. */
export const CC_REPORT_AUDIENCES: readonly { readonly audience: BenchmarkReportAudience; readonly label: string }[] = [
  { audience: BenchmarkReportAudience.ExecutiveSummary, label: 'Executive Summary' },
  { audience: BenchmarkReportAudience.TechnicalReport, label: 'Report for AI Researchers and Developers' },
  { audience: BenchmarkReportAudience.InternalBrief, label: 'Internal Brief' },
  { audience: BenchmarkReportAudience.ProviderIssueReport, label: 'Provider Issue Report' }
];

/** The start body: the run report-writing request, with the Provider Issue Report among the audiences. */
export interface CcWriteReportsRequest extends Omit<WriteRunReportDocumentsRequest, 'audiences'> {
  audiences?: BenchmarkReportAudience[];
  /** Confirms writing from an out-of-date analysis; without it the server answers 409 with `outOfDate: true`. */
  acknowledgeOutOfDate?: boolean;
}

export interface CcReportEstimateRequest {
  writerModelConfigurationId: number;
  audiences?: BenchmarkReportAudience[];
}

/** The run report estimate, and whether the Provider Issue Report can be written for this analysis. */
export interface CcReportEstimate extends BenchmarkRunReportEstimateDto {
  providerIssueReportAvailable: boolean;
  providerIssueReportReason: string | null;
}

/** The chat consistency report figures uploaded with every written document, in document order. */
export const CC_REPORT_FIGURE_KEYS = ['cc1-quality', 'cc2-speed', 'cc3-work', 'cc4-timeline'] as const;
export type CcReportFigureKey = typeof CC_REPORT_FIGURE_KEYS[number];
