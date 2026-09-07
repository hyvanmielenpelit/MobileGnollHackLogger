/**
 * The wire contract of `GET /api/admin/benchmark/model-comparison`, and the adapter that turns one
 * response into the chart core's own input shape.
 *
 * It sits in its own file rather than in the component because two layers consume it: the view
 * renders from it, and the service fetches into it. Pure TypeScript throughout — no Angular, no DOM
 * — so it unit-tests without a fixture and neither consumer has to import the other.
 *
 * The interfaces mirror `Overseer/Models/BenchmarkModelComparisonModels.cs` field for field, in the
 * camel case ASP.NET Core serialises the C# names to. Every field the server declares nullable is
 * nullable here, and nothing is computed that the server did not already compute.
 */

import type { BenchmarkComparabilityDifferenceDto } from '../../../services/admin-benchmark.service';
import type { ModelComparisonContext, ModelComparisonEntry } from './model-comparison-charts';

export type { BenchmarkComparabilityDifferenceDto };

/** The admin endpoint the view reads. Named here so the service and the spec cannot disagree on it. */
export const MODEL_COMPARISON_ENDPOINT = '/api/admin/benchmark/model-comparison';

/**
 * Which price card every entry's candidate cost is computed from.
 *
 * `Current` re-prices every entry from today's catalog, which is the only basis on which two runs
 * months apart compare models rather than price cards. `AsRun` is honest about what was actually
 * spent and is flagged cost-degraded whenever the snapshots differ.
 */
export type BenchmarkModelComparisonPricingBasis = 'AsRun' | 'Current';

/** `Comparable`, `Degraded` or `Excluded`, as the server spells them. */
export type BenchmarkModelComparisonState = 'Comparable' | 'Degraded' | 'Excluded';

/** What to compare: single runs at R = 1, and analysis groups at one point each. */
export interface BenchmarkModelComparisonQuery {
  readonly runIds: readonly number[];
  readonly groupIds: readonly number[];
  readonly pricingBasis: BenchmarkModelComparisonPricingBasis;
}

/**
 * The query string for one comparison request, as repeated `runIds` / `groupIds` parameters plus the
 * basis — the shape `[FromQuery] BenchmarkModelComparisonRequest` binds from.
 */
export function modelComparisonQueryParams(query: BenchmarkModelComparisonQuery): [string, string][] {
  const params: [string, string][] = [];
  for (const runId of query.runIds) {
    params.push(['runIds', String(runId)]);
  }
  for (const groupId of query.groupIds) {
    params.push(['groupIds', String(groupId)]);
  }
  params.push(['pricingBasis', query.pricingBasis]);
  return params;
}

/** The quality axis: the Intelligence Index with its 95 % interval and the two components behind it. */
export interface BenchmarkModelComparisonQualityDto {
  pointEstimate: number;
  /** Items behind the estimate, and the set's items-per-run once every Fundamental key matches. */
  itemCount: number;
  intervalHalfWidth?: number | null;
  intervalLower?: number | null;
  intervalUpper?: number | null;
  /** A bound hit the 0-100 score range, so the rendered interval is narrower than the half-width. */
  intervalTruncated: boolean;
  /** Item sampling: would a different draw of questions move this? It does not shrink with R. */
  itemSamplingHalfWidth?: number | null;
  /** Reproducibility: would a re-run move this? Null below three runs. */
  reproducibilityHalfWidth?: number | null;
  reproducibilityStandardDeviation?: number | null;
  reproducibilityAvailable: boolean;
  /** Ready to render under the interval: which sources of variation it actually covers. */
  intervalBasis: string;
}

/** The speed axis: time to first token, which is the latency a chat user perceives. */
export interface BenchmarkModelComparisonSpeedDto {
  ttftP50Ms?: number | null;
  ttftP90Ms?: number | null;
  /** Answers that reported a time to first token — its own denominator, smaller than the pooled one. */
  ttftAnswerCount: number;
  modelTimeP50Ms?: number | null;
  modelTimeP90Ms?: number | null;
  pooledAnswerCount: number;
  degraded: boolean;
  degradedReason?: string | null;
  caveat: string;
}

/**
 * The cost axis: candidate spend per question, in USD.
 *
 * Candidate-only by design — grading-role spend is most of a run's cost and none of it transfers to
 * the chat assistant — which is why no field here totals a run including its graders.
 */
export interface BenchmarkModelComparisonCostDto {
  /** Null unless a price card resolved for every run behind the entry; an unknown cost is never zero. */
  candidateCostPerQuestionUsd?: number | null;
  candidateCostPerRunUsd?: number | null;
  candidateTotalCostUsd?: number | null;
  /** `AsRun` or `Current`, echoing the basis the whole comparison was computed on. */
  basis: string;
  pricingAsOf?: string | null;
  pricingResolved: boolean;
  degraded: boolean;
  degradedReason?: string | null;
  /** `yyyy-MM-dd`: a cost ranking that flips on a known future date has an expiry on it. */
  scheduledChangeEffectiveFrom?: string | null;
  scheduledChangeNote?: string | null;
}

/** Figures that belong in the table and in no chart, each for a stated reason. */
export interface BenchmarkModelComparisonTableDto {
  meanSpeedIndex?: number | null;
  /** Half the scored answers finished inside target, so the index cannot discriminate at this speed. */
  speedIndexSaturated: boolean;
  speedIndexCeilingAnswerCount: number;
  speedIndexScoredAnswerCount: number;
  /** A ratio of two noisy estimators: no simple interval, and its meaning inverts near zero. */
  costPerIndexPointUsd?: number | null;
  meanStoredQualityIndex?: number | null;
  unstableItemCount: number;
}

/** One model, as configured, on one comparison. */
export interface BenchmarkModelComparisonEntryDto {
  /** `run:12` or `group:3` — stable across a refresh, and what every figure keys its series by. */
  key: string;
  /** `Run` or `Group`. */
  sourceKind: string;
  sourceId: number;
  sourceName?: string | null;
  runIds: number[];
  /** R: runs behind this point. */
  runCount: number;
  suiteId?: number | null;
  suiteName?: string | null;

  provider: string;
  modelId: string;
  modelDisplayName: string;
  thinkingLevel?: string | null;
  reasoningMode?: string | null;
  reasoningSummary?: string | null;
  serviceTier?: string | null;
  maxOutputTokens?: number | null;
  parallelExecutionMode: string;

  /** A short axis label: the display name, plus the thinking level when the set mixes them. */
  label: string;

  firstRunStartedAtUtc: string;
  lastRunStartedAtUtc: string;

  state: BenchmarkModelComparisonState;
  comparable: boolean;
  /** The entry measured something else. All four measure objects are null when this is set. */
  excluded: boolean;
  speedDegraded: boolean;
  costDegraded: boolean;

  excludingKeys: string[];
  speedDegradingKeys: string[];
  costDegradingKeys: string[];
  differences: BenchmarkComparabilityDifferenceDto[];
  explanation: string;

  quality?: BenchmarkModelComparisonQualityDto | null;
  speed?: BenchmarkModelComparisonSpeedDto | null;
  cost?: BenchmarkModelComparisonCostDto | null;
  table?: BenchmarkModelComparisonTableDto | null;
}

/** A measure the comparison deliberately refuses to chart, and the reason. */
export interface BenchmarkModelComparisonExcludedMeasureDto {
  measure: string;
  reason: string;
  /** Where the reader should look instead. */
  instead: string;
}

/** A cross-model comparison: one point per model, the basis they were costed on, and the refusals. */
export interface BenchmarkModelComparisonDto {
  pricingBasis: string;
  /** Ready for a figure subtitle, including the date the basis was taken. */
  pricingBasisLabel: string;
  computedAtUtc: string;
  baselineSuiteId?: number | null;
  baselineSuiteName?: string | null;
  /** The entries that define the baseline condition — those that may be charted. */
  baselineEntryKeys: string[];
  /** The baseline's value for every must-match key, so a report can print the condition. */
  baselineKeyValues: Record<string, string>;
  /** The keys the points are allowed to differ on, for the view's own explanatory text. */
  modelAxisKeys: string[];
  entries: BenchmarkModelComparisonEntryDto[];
  comparableCount: number;
  excludedCount: number;
  thinkingLevelsDiffer: boolean;
  speedAxisCaveat?: string | null;
  explanation: string;
  excludedMeasures: BenchmarkModelComparisonExcludedMeasureDto[];
}

// ---------------------------------------------------------------------------------------------
// The adapter onto the chart core's input shape
// ---------------------------------------------------------------------------------------------

/**
 * Three fields the chart core declares that this endpoint does not carry, named once here so the
 * view can say so out loud rather than each caller rediscovering it:
 *
 * - **`speedIndexSd`** — no per-run Speed Index dispersion is returned, so the speed panel draws no
 *   interval under that measure and the saturation notice carries the caveat instead.
 * - **`candidateCostPerQuestionSdUsd`** — the cost object carries no dispersion field at any R, so a
 *   cost bar never gets an interval and an R = 1 entry keeps its explicit `n = 1` marker.
 * - **`totalRunCostUsd`** — the cost object is candidate-only by design, because grading-role spend
 *   is not transferable to the chat assistant. There is no run total including graders to switch to.
 */
export const UNSUPPLIED_CHART_FIELDS: readonly string[] = [
  'speedIndexSd',
  'candidateCostPerQuestionSdUsd',
  'totalRunCostUsd',
];

/**
 * The value stood in for a measure the server did not supply.
 *
 * `NaN`, never `0`: zero is a measurement and this is the absence of one. A zero cost would plot as
 * a bar touching the baseline and read as "free"; `NaN` draws nothing, and if one ever reached a
 * figure the gap would be visible rather than plausible. Excluded entries carry it in every numeric
 * field, which is the invariant the server establishes by nulling all four measure objects on them.
 */
export const UNMEASURED = Number.NaN;

/** Which of the three axes an entry has no number for. Empty for a fully measured entry. */
export function unmeasuredAxes(entry: BenchmarkModelComparisonEntryDto): string[] {
  const missing: string[] = [];
  if (entry.quality == null) {
    missing.push('quality');
  }
  if (entry.speed == null || entry.speed.ttftP50Ms == null) {
    missing.push('speed');
  }
  if (entry.cost == null || entry.cost.candidateCostPerQuestionUsd == null) {
    missing.push('cost');
  }
  return missing;
}

/**
 * Adapts one response onto the chart core's `ModelComparisonEntry`.
 *
 * Every entry is mapped, excluded ones included: they are counted, named and tabulated, and the
 * chart core removes them before any figure sees them ({@link ModelComparisonEntry.excluded} gates
 * `selectPlottedEntries`). Dropping them here instead would make an unchartable model invisible,
 * which is the exact failure this feature exists to prevent — a reader trusting six charts without
 * checking what was left out of them.
 */
export function toChartEntries(dto: BenchmarkModelComparisonDto | null): ModelComparisonEntry[] {
  if (dto == null) {
    return [];
  }
  return dto.entries.map(entry => ({
    key: entry.key,
    label: entry.label || entry.modelDisplayName || entry.modelId,
    runCount: entry.runCount,

    intelligenceIndex: entry.quality?.pointEstimate ?? UNMEASURED,
    // Null half-width means neither uncertainty component could be computed, which is not the same
    // as a half-width of zero — that would draw a bare point estimate and read as certainty.
    intelligenceIndexCi95HalfWidth: entry.quality?.intervalHalfWidth ?? UNMEASURED,

    speedIndex: entry.table?.meanSpeedIndex ?? null,
    speedIndexSaturated: entry.table?.speedIndexSaturated ?? false,
    speedIndexSd: null,

    ttftP50Ms: entry.speed?.ttftP50Ms ?? UNMEASURED,
    ttftP90Ms: entry.speed?.ttftP90Ms ?? UNMEASURED,

    candidateCostPerQuestionUsd: entry.cost?.candidateCostPerQuestionUsd ?? UNMEASURED,
    candidateCostPerQuestionSdUsd: null,
    totalRunCostUsd: UNMEASURED,
    totalRunCostSdUsd: null,

    speedDegraded: entry.speedDegraded,
    costDegraded: entry.costDegraded,
    excluded: entry.excluded,
    excludedReasonKeys: entry.excludingKeys,
  }));
}

/**
 * The set-level facts the figures put in their subtitles.
 *
 * `itemsPerRun` comes from the first charted entry's item count rather than from a set-level field,
 * because the server carries it per entry; every Fundamental key must match for an entry to be
 * charted at all, and the suite is one of them, so the charted entries agree on it by construction.
 */
export function toChartContext(dto: BenchmarkModelComparisonDto | null): ModelComparisonContext {
  const charted = dto?.entries.filter(entry => !entry.excluded && entry.quality != null) ?? [];
  return {
    itemsPerRun: charted[0]?.quality?.itemCount ?? 0,
    pricingBasisLabel: dto?.pricingBasisLabel || dto?.pricingBasis || 'Unknown pricing basis',
    suiteName: dto?.baselineSuiteName ?? '',
  };
}
