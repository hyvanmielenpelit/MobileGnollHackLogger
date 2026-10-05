// The statistics records of a battery analysis (`BenchmarkBatteryStatisticsResult` and
// `BenchmarkBatteryComparison` on the server), as the API serializes them: camelCase, the enums as
// strings. Every nullable C# figure is nullable here. The nested group statistics and group
// comparison records are kept opaque (`unknown`): the battery views read none of their fields.
//
// This file imports nothing from the service, which imports these types from here.

import type { CardListSort } from '../../../shared/data-table/card-list-state';
import { RunFactBadge, runFactBadges } from '../run-report-frame/run-facts';

/** `BenchmarkBatteryWeightingScheme` as the API sends it. */
export type BatteryWeightingSchemeKey = 'DifficultyMass' | 'ItemCount' | 'Equal' | 'Custom';

/** `BenchmarkBatteryReproducibilitySource`. */
export type BenchmarkBatteryReproducibilitySource = 'NotAvailable' | 'Rounds' | 'PerSuiteFallback';

/** `BenchmarkBatteryRandomizationMethod`. */
export type BenchmarkBatteryRandomizationMethod = 'NotComputed' | 'Exact' | 'MonteCarlo';

export interface BenchmarkBatterySuiteMass {
  itemCount: number;
  difficultyMass: number;
}

export interface BenchmarkBatteryExcludedMember {
  suiteIndex: number;
  round: number;
  runId: number;
  reason: string;
}

export interface BenchmarkBatteryRoundIndex {
  round: number;
  index: number;
}

export interface BenchmarkBatteryRunIndex {
  runId: number;
  round: number | null;
  index: number;
}

export interface BenchmarkBatteryOverallIndex {
  pointEstimate: number;
  itemSamplingStandardError: number | null;
  /** Welch–Satterthwaite ν, unfloored. */
  effectiveDegreesOfFreedom: number | null;
  itemSamplingCriticalValue: number | null;
  itemSamplingHalfWidth: number | null;
  itemSamplingWithheldBySuiteIndex: number[];
  reproducibilityStandardError: number | null;
  reproducibilityDegreesOfFreedom: number | null;
  reproducibilityCriticalValue: number | null;
  reproducibilityHalfWidth: number | null;
  reproducibilitySource: BenchmarkBatteryReproducibilitySource;
  roundCount: number;
  perRoundIndices: BenchmarkBatteryRoundIndex[];
  combinedHalfWidth: number | null;
  combinedLower: number | null;
  combinedUpper: number | null;
  combinedIntervalTruncated: boolean;
}

export interface BenchmarkBatterySuiteProfile {
  suiteIndex: number;
  suiteId: number;
  suiteName: string;
  complete: boolean;
  weight: number | null;
  countWeight: number | null;
  examItemCount: number;
  difficultyMass: number;
  expectedQuestionCount: number;
  examIncomplete: boolean;
  scoredItemCount: number;
  usableMemberCount: number;
  index: number | null;
  contribution: number | null;
  itemSamplingStandardError: number | null;
  reproducibilityStandardError: number | null;
  combinedHalfWidth: number | null;
  combinedLower: number | null;
  combinedUpper: number | null;
  combinedIntervalTruncated: boolean;
  identityHolds: boolean;
  runIndices: BenchmarkBatteryRunIndex[];
  meanSpeedIndex: number | null;
  criticalErrorRate: number | null;
  totalCost: number | null;
  meanCostPerRun: number | null;
  /** The suite's `BenchmarkGroupStatisticsResult`; not read by the client. */
  statistics: unknown | null;
}

export interface BenchmarkBatterySchemeIndex {
  scheme: BatteryWeightingSchemeKey;
  declared: boolean;
  weights: number[];
  index: number;
}

export interface BenchmarkBatteryLeaveOneOut {
  suiteIndex: number;
  suiteName: string;
  index: number | null;
  change: number | null;
}

export interface BenchmarkBatteryDimension {
  dimension: string;
  mean: number | null;
  withheldBySuiteIndex: number[];
}

export interface BenchmarkBatterySpeedStatistics {
  overallSpeedIndex: number | null;
  speedIndexWithheldBySuiteIndex: number[];
  pooledAnswerCount: number;
  modelTimeP50Ms: number | null;
  modelTimeP90Ms: number | null;
  modelTimeMaxMs: number | null;
  modelTimeMeanMs: number | null;
  totalModelTimeMs: number;
  ttftAnswerCount: number;
  ttftP50Ms: number | null;
  ttftP90Ms: number | null;
  ttftMaxMs: number | null;
  degraded: boolean;
  degradedReason: string | null;
}

export interface BenchmarkBatteryCostStatistics {
  available: boolean;
  withheldBySuiteIndex: number[];
  withheldReason: string | null;
  totalCost: number | null;
  passCost: number | null;
  totalCostByRole: Record<string, number> | null;
  passCostByRole: Record<string, number> | null;
  answerRowCount: number | null;
  costPerQuestion: number | null;
  costPerIndexPoint: number | null;
  degraded: boolean;
  degradedReason: string | null;
}

export interface BenchmarkBatteryUsageStatistics {
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCacheReadTokens: number;
  totalAssessmentInputTokens: number;
  totalAssessmentOutputTokens: number;
  totalClaimVerificationInputTokens: number;
  totalClaimVerificationOutputTokens: number;
  totalToolCalls: number;
  totalModelCalls: number | null;
  toolCallsByFamily: Record<string, number>;
  claimsSupported: number;
  claimsRefuted: number;
  claimsIndeterminate: number;
  claimsChecked: number;
}

export interface BenchmarkBatteryStatisticsResult {
  methodVersion: number;
  complete: boolean;
  completedSuiteCount: number;
  suiteCount: number;
  scheme: BatteryWeightingSchemeKey;
  weights: number[];
  countWeights: number[];
  suiteMasses: BenchmarkBatterySuiteMass[];
  excludedMembers: BenchmarkBatteryExcludedMember[];
  pooledIdentityHolds: boolean;
  overallIndex: BenchmarkBatteryOverallIndex | null;
  suites: BenchmarkBatterySuiteProfile[];
  betweenSuiteStandardDeviation: number | null;
  betweenSuiteRange: number | null;
  weightingSensitivity: BenchmarkBatterySchemeIndex[];
  leaveOneSuiteOut: BenchmarkBatteryLeaveOneOut[];
  dimensions: BenchmarkBatteryDimension[];
  criticalErrorRate: number | null;
  speed: BenchmarkBatterySpeedStatistics | null;
  cost: BenchmarkBatteryCostStatistics | null;
  usage: BenchmarkBatteryUsageStatistics | null;
  caveats: string[];
}

export interface BenchmarkBatterySuiteComparison {
  suiteIndex: number;
  suiteName: string;
  weight: number | null;
  pairedItemCount: number;
  weightedDifference: number | null;
  weightedDifferenceStandardError: number | null;
  wilcoxonPValue: number | null;
  holmAdjustedPValue: number | null;
  /** The suite's `BenchmarkGroupComparison`; not read by the client. */
  comparison: unknown | null;
  note: string | null;
}

export interface BenchmarkBatteryComparison {
  methodVersion: number;
  baselineOverallIndex: number | null;
  treatmentOverallIndex: number | null;
  suites: BenchmarkBatterySuiteComparison[];
  pairedItemCount: number;
  compositeDifference: number | null;
  compositeWithheldBySuiteIndex: number[];
  compositeStandardError: number | null;
  standardErrorWithheldBySuiteIndex: number[];
  compositeDegreesOfFreedom: number | null;
  compositeCriticalValue: number | null;
  compositeConfidenceHalfWidth: number | null;
  compositeConfidenceLower: number | null;
  compositeConfidenceUpper: number | null;
  randomizationPValue: number | null;
  randomizationMethod: BenchmarkBatteryRandomizationMethod;
  monteCarloResamples: number | null;
  monteCarloStandardError: number | null;
  seed: number | null;
  notes: string[];
}

// --- Weighting schemes -----------------------------------------------------------------------

export interface BatterySchemeOption {
  readonly value: BatteryWeightingSchemeKey;
  readonly label: string;
}

/** The schemes in the order the editor and the reports list them; the first is the default. */
export const BATTERY_SCHEME_OPTIONS: readonly BatterySchemeOption[] = [
  { value: 'DifficultyMass', label: 'Questions and difficulty' },
  { value: 'ItemCount', label: 'Questions only' },
  { value: 'Equal', label: 'Equal per suite' },
  { value: 'Custom', label: 'Custom' }
];

export const DEFAULT_BATTERY_SCHEME: BatteryWeightingSchemeKey = 'DifficultyMass';

/** The weight of a question whose difficulty was never assessed, as the server counts it. */
export const DEFAULT_QUESTION_WEIGHT = 50;

export function batterySchemeLabel(scheme: string | null | undefined): string {
  return BATTERY_SCHEME_OPTIONS.find(option => option.value === scheme)?.label ?? (scheme ?? '');
}

/** One question's weight in a suite's difficulty mass: its assessed difficulty, else 50, never below 1. */
export function questionWeight(assessedDifficulty: number | null | undefined): number {
  return Math.max(1, assessedDifficulty ?? DEFAULT_QUESTION_WEIGHT);
}

/**
 * The normalized suite weights `scheme` gives, in suite order, as `BenchmarkBatteryDefinition.Weights`
 * computes them on the server. Null when the weights are undefined: no suites, a mass that is not a
 * finite non-negative number, a zero total, or (under Custom) a weight that is missing, not finite or
 * not above zero.
 */
export function previewBatteryWeights(
  scheme: BatteryWeightingSchemeKey,
  masses: readonly BenchmarkBatterySuiteMass[],
  customWeights: readonly (number | null | undefined)[] = []
): number[] | null {
  if (masses.length === 0) {
    return null;
  }
  switch (scheme) {
    case 'DifficultyMass':
      return normalizeWeights(masses.map(m => m.difficultyMass));
    case 'ItemCount':
      return normalizeWeights(masses.map(m => m.itemCount));
    case 'Equal':
      return masses.map(() => 1 / masses.length);
    case 'Custom': {
      if (customWeights.length !== masses.length || customWeights.some(w => !isValidCustomWeight(w))) {
        return null;
      }
      return normalizeWeights(customWeights.map(w => w as number));
    }
    default:
      return null;
  }
}

export function isValidCustomWeight(weight: number | null | undefined): boolean {
  return typeof weight === 'number' && Number.isFinite(weight) && weight > 0;
}

function normalizeWeights(raw: readonly number[]): number[] | null {
  if (raw.some(v => !Number.isFinite(v) || v < 0)) {
    return null;
  }
  const total = raw.reduce((sum, v) => sum + v, 0);
  if (!(total > 0)) {
    return null;
  }
  return raw.map(v => v / total);
}

// --- Labels ----------------------------------------------------------------------------------

export function reproducibilitySourceLabel(source: BenchmarkBatteryReproducibilitySource | string | null | undefined): string {
  switch (source) {
    case 'Rounds': return 'Per-round composites';
    case 'PerSuiteFallback': return 'Per-suite fallback (assumes independent suites)';
    case 'NotAvailable': return 'Not available (fewer than three complete rounds)';
    default: return source ?? '';
  }
}

export function randomizationMethodLabel(method: BenchmarkBatteryRandomizationMethod | string | null | undefined): string {
  switch (method) {
    case 'Exact': return 'Exact enumeration';
    case 'MonteCarlo': return 'Monte Carlo';
    case 'NotComputed': return 'Not computed';
    default: return method ?? '';
  }
}

/** A battery run's `BenchmarkRunSeriesStatus` as the UI shows it. */
export function batteryRunStatusLabel(status: string | null | undefined): string {
  switch (status) {
    case 'WaitingForCap': return 'Waiting for run cap';
    case 'CompletedWithErrors': return 'Completed with errors';
    case 'Cancelled': return 'Canceled';
    default: return status ?? '';
  }
}

/** Pending, Running and WaitingForCap: the orchestrator may still move the battery run. */
export function isLiveBatteryRunStatus(status: string | null | undefined): boolean {
  return status === 'Pending' || status === 'Running' || status === 'WaitingForCap';
}

export function isFinishedBatteryRunStatus(status: string | null | undefined): boolean {
  return status === 'Completed' || status === 'CompletedWithErrors';
}

// --- Post-run work -----------------------------------------------------------------------------

/** The fields of a battery run its post-run work is read from. */
export interface BatteryPostRunFields {
  readonly latestAnalysisId?: number | null;
  readonly analysisStale?: boolean;
  /**
   * `BenchmarkBatteryRunDto.postRunWork`: `None`, `Repairing`, `Analysing` or `WritingReports`.
   * Missing (an older server) reads as `None`.
   */
  readonly postRunWork?: string | null;
}

/** A battery run's `BenchmarkRunReportDocumentsStatus` by name, from its number or its name. */
export type BatteryReportDocumentsStatusName =
  | 'NotRequested' | 'Pending' | 'Writing' | 'Completed' | 'CompletedWithWarnings' | 'Failed' | 'Skipped' | 'Canceled';

const REPORT_DOCUMENTS_STATUS_NAMES: readonly BatteryReportDocumentsStatusName[] = [
  'NotRequested', 'Pending', 'Writing', 'Completed', 'CompletedWithWarnings', 'Failed', 'Skipped', 'Canceled'
];

export function batteryReportDocumentsStatusName(status: number | string | null | undefined): BatteryReportDocumentsStatusName {
  if (typeof status === 'number') {
    return REPORT_DOCUMENTS_STATUS_NAMES[status] ?? 'NotRequested';
  }
  return REPORT_DOCUMENTS_STATUS_NAMES.find(name => name === status) ?? 'NotRequested';
}

/** The battery analysis of the finished members is still to come: none yet, or one the members have outdated. */
export function batteryAnalysisPending(run: BatteryPostRunFields): boolean {
  return run.latestAnalysisId == null || run.analysisStale === true;
}

/** What the server is still doing for the battery run; `None` for a missing run or field. */
export function batteryPostRunWork(run: BatteryPostRunFields | null | undefined): string {
  return run?.postRunWork || 'None';
}

/**
 * The server is still working on the battery run outside its drive loop: repairing a member,
 * computing the analysis or writing the AI reports. Pollers keep following the battery run while
 * this holds.
 */
export function batteryAwaitsPostRun(run: BatteryPostRunFields | null | undefined): boolean {
  return batteryPostRunWork(run) !== 'None';
}

// --- The model under test --------------------------------------------------------------------

/** The fields of a battery run its model under test is read from. */
export interface BatteryModelFields {
  readonly testedModelLabel?: string | null;
  readonly testedModelId?: string | null;
  readonly testedProvider?: string | null;
  readonly testedThinkingLevel?: string | null;
  readonly testedReasoningMode?: string | null;
  readonly testedServiceTier?: string | null;
}

/** A battery run's model under test: its label, else its id. */
export function batteryModelName(battery: BatteryModelFields): string {
  return battery.testedModelLabel || battery.testedModelId || 'Model not recorded';
}

const batteryBadgeCache = new WeakMap<BatteryModelFields, RunFactBadge[]>();

/** The badges of a battery run's model under test, by the same rules as a run card's, built once per object. */
export function batteryModelBadges(battery: BatteryModelFields): RunFactBadge[] {
  let badges = batteryBadgeCache.get(battery);
  if (!badges) {
    badges = runFactBadges({
      name: batteryModelName(battery),
      provider: battery.testedProvider || null,
      thinkingLevel: battery.testedThinkingLevel ?? null,
      reasoningMode: battery.testedReasoningMode ?? null,
      serviceTier: battery.testedServiceTier ?? null,
      customEndpoint: false
    });
    batteryBadgeCache.set(battery, badges);
  }
  return badges;
}

// --- The Batteries card list -----------------------------------------------------------------

/** Where the Batteries list remembers its Sort by order. */
export const BATTERY_LIST_VIEW_STORAGE_KEY = 'overseer.benchmark.batteries.view';

/** The orders the Batteries list's Sort by offers. */
export const BATTERY_LIST_SORTS: readonly CardListSort[] = [
  { id: 'modified', label: 'Recently modified', column: 'modified', direction: 'desc' },
  { id: 'name', label: 'Name (A–Z)', column: 'name', direction: 'asc' },
  { id: 'runs', label: 'Most runs', column: 'runs', direction: 'desc' },
  { id: 'suites', label: 'Most suites', column: 'suites', direction: 'desc' }
];

export const DEFAULT_BATTERY_LIST_SORT = 'modified';

/** The Batteries list shows its filter bar only above this many batteries. */
export const BATTERY_FILTER_BAR_MIN_EXCLUSIVE = 3;

/** The suite rows a battery card shows before *Show all K suites*. */
export const BATTERY_CARD_SUITE_ROWS = 4;

/** One segment of a battery card's stacked weight bar, in run order. */
export interface BatteryWeightSegment {
  readonly index: number;
  /** The segment's share of the bar's width, 0–1. */
  readonly share: number;
  readonly deleted: boolean;
}

/**
 * The stacked weight bar of a battery card: one segment per suite in run order, each as wide as
 * its declared weight. Equal shares when the declared weights are undefined (one per suite), so
 * the bar still shows the suite count and any deleted suite.
 */
export function batteryWeightMix(
  suites: readonly { readonly index: number; readonly deleted: boolean }[],
  declaredWeights: readonly number[]
): BatteryWeightSegment[] {
  const declared = declaredWeights.length === suites.length && declaredWeights.every(w => Number.isFinite(w) && w >= 0);
  return suites.map((suite, position) => ({
    index: suite.index,
    share: declared ? declaredWeights[position] : 1 / suites.length,
    deleted: suite.deleted
  }));
}

/** What an *Index withheld* grid cell tells the operator to do. */
export const INDEX_WITHHELD_HINT =
  'Re-run failed questions on this run; the battery run follows the re-run when it finishes.';

// --- Number formatting -----------------------------------------------------------------------

const DASH = '—';

/** A fixed-point number, or an em dash for null. */
export function formatNumber(value: number | null | undefined, digits = 1): string {
  return value === null || value === undefined || !Number.isFinite(value) ? DASH : value.toFixed(digits);
}

/** A signed fixed-point number (`+1.2`, `−0.4`), or an em dash for null. */
export function formatSigned(value: number | null | undefined, digits = 1): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return DASH;
  }
  const fixed = Math.abs(value).toFixed(digits);
  if (Number(fixed) === 0) {
    return fixed;
  }
  return (value > 0 ? '+' : '−') + fixed;
}

/** A weight in 0–1 as a percentage with one decimal, or an em dash. */
export function formatPercent(weight: number | null | undefined, digits = 1): string {
  return weight === null || weight === undefined || !Number.isFinite(weight) ? DASH : `${(weight * 100).toFixed(digits)} %`;
}

/** A p-value: three significant figures, `< 0.001` below that. */
export function formatPValue(p: number | null | undefined): string {
  if (p === null || p === undefined || !Number.isFinite(p)) {
    return DASH;
  }
  if (p < 0.001) {
    return '< 0.001';
  }
  return p.toFixed(3);
}

/** A dollar amount with two to four decimals, or an em dash. */
export function formatCost(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return DASH;
  }
  const digits = Math.abs(value) >= 1 ? 2 : 4;
  return `$${value.toFixed(digits)}`;
}

/** Milliseconds as `850 ms`, `12.3 s` or `4 min 05 s`. */
export function formatMs(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms)) {
    return DASH;
  }
  if (ms < 1000) {
    return `${Math.round(ms)} ms`;
  }
  if (ms < 60_000) {
    return `${(ms / 1000).toFixed(1)} s`;
  }
  const totalSeconds = Math.round(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const ss = seconds.toString().padStart(2, '0');
  return hours > 0 ? `${hours} h ${minutes.toString().padStart(2, '0')} min` : `${minutes} min ${ss} s`;
}

/** `72.4 ± 5.1`, or the point alone without a half-width, or an em dash. */
export function formatIndexWithHalfWidth(point: number | null | undefined, halfWidth: number | null | undefined): string {
  if (point === null || point === undefined || !Number.isFinite(point)) {
    return DASH;
  }
  return halfWidth === null || halfWidth === undefined || !Number.isFinite(halfWidth)
    ? point.toFixed(1)
    : `${point.toFixed(1)} ± ${halfWidth.toFixed(1)}`;
}

/** `[67.3, 77.5]`, or an em dash when either bound is missing. */
export function formatInterval(lower: number | null | undefined, upper: number | null | undefined, digits = 1): string {
  if (lower === null || lower === undefined || upper === null || upper === undefined) {
    return DASH;
  }
  return `[${lower.toFixed(digits)}, ${upper.toFixed(digits)}]`;
}

/** The server message of an HTTP error: its text body, else its `message`, else `fallback`. */
export function httpErrorText(err: unknown, fallback: string): string {
  const e = err as { error?: unknown; message?: string } | null | undefined;
  if (typeof e?.error === 'string' && e.error.trim() !== '') {
    return e.error;
  }
  const inner = e?.error as { message?: string; title?: string; detail?: string } | null | undefined;
  return inner?.message || inner?.detail || inner?.title || fallback;
}
