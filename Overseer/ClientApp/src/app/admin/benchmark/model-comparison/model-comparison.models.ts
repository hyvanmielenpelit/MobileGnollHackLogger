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

/** The admin endpoint for the comparability index, named for the same reason as the endpoint above. */
export const MODEL_COMPARABILITY_INDEX_ENDPOINT = '/api/admin/benchmark/model-comparison/comparability';

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
  /** The baseline condition's must-match signature. Empty when nothing reached the baseline. */
  baselineSignature: string;
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
// The comparability index: which runs and analysis groups may share a chart, before Compare
// ---------------------------------------------------------------------------------------------

/**
 * One run or analysis group as the comparability index places it. Mirrors
 * `BenchmarkComparabilityIndexEntryDto` field for field.
 */
export interface BenchmarkComparabilityIndexEntryDto {
  /** `run:12` or `group:3` — the same key an entry in the comparison response carries. */
  key: string;
  sourceKind: string;
  sourceId: number;
  /** 1-based, largest cohort first then first appearance. 0 means not assigned to any condition. */
  conditionOrdinal: number;
  /** "Condition A" and so on, or "Self-inconsistent". */
  conditionLabel: string;
  signature: string;
  /** A group whose own members disagree on a must-match or model-axis key. */
  selfInconsistent: boolean;
  selfInconsistentKeys: string[];
  differencesFromLargest: BenchmarkComparabilityDifferenceDto[];
  /** Degrading key value: question parallelism as run. */
  questionParallelism: string;
  /** Degrading key value: the pricing snapshot as run. */
  pricingSnapshot: string;
}

/**
 * One must-match comparability key as a condition holds it: what the key is, and its value.
 *
 * The legend reads a whole condition as a methods statement from these — the machine name the rest
 * of the product uses, the phrase an operator reads it by, the one line saying what a difference
 * would cost, and the value every source in the condition agreed on.
 */
export interface BenchmarkComparabilityKeyValueDto {
  name: string;
  /** A short noun phrase — "Question suite", "Candidate system prompt". */
  label: string;
  /** One line: what a difference on this key would mean for a comparison. */
  description: string;
  /** `Fundamental`, `Candidate`, `Instrument` or `SpeedAndCost`. */
  kind: string;
  /** `Text`, `Identifier`, `Hash`, `Json` or `List` — the shape of `value`, not its meaning. */
  valueKind: string;
  /** The canonical value compared for equality, verbatim. Never abbreviated on the wire. */
  value: string;
  /** A friendlier rendering the client cannot derive — the suite's name beside its id; else null. */
  displayValue?: string | null;
}

/** One cohort of the index: everything in it agrees on every must-match comparability key. */
export interface BenchmarkComparabilityConditionDto {
  ordinal: number;
  label: string;
  sourceCount: number;
  runCount: number;
  /** The must-match signature every source in this cohort shares, in full. */
  signature: string;
  /** The most recent run in the cohort, so a majority cohort on a superseded instrument is visibly stale. */
  newestRunStartedAtUtc?: string | null;
}

/** The comparability index for the runs and groups currently on offer. */
export interface BenchmarkComparabilityIndexDto {
  computedAtUtc: string;
  entries: BenchmarkComparabilityIndexEntryDto[];
  conditions: BenchmarkComparabilityConditionDto[];
  /** The largest condition's must-match keys, in canonical key order, described for the legend. */
  largestConditionKeys: BenchmarkComparabilityKeyValueDto[];
  /** How the reference condition was chosen, in one sentence the server owns. */
  referenceSelectionRule: string;
  mustMatchKeyNames: string[];
  modelAxisKeyNames: string[];
  degradingKeyNames: string[];
}

/** What to index: the runs and analysis groups currently on offer. */
export interface BenchmarkComparabilityIndexQuery {
  readonly runIds: readonly number[];
  readonly groupIds: readonly number[];
}

/**
 * The query string for one comparability-index request, as repeated `runIds` / `groupIds`
 * parameters — the shape `[FromQuery] BenchmarkComparabilityIndexRequest` binds from.
 */
export function comparabilityIndexQueryParams(query: BenchmarkComparabilityIndexQuery): [string, string][] {
  const params: [string, string][] = [];
  for (const runId of query.runIds) {
    params.push(['runIds', String(runId)]);
  }
  for (const groupId of query.groupIds) {
    params.push(['groupIds', String(groupId)]);
  }
  return params;
}

/**
 * The condition ordinal for one entry key (`run:12` / `group:3`), or null when the index has not
 * loaded or the key is not in it. An ordinal of 0 means "not assigned" on the wire; this folds
 * that into null too, so a caller never has to know the encoding.
 */
export function conditionOf(index: BenchmarkComparabilityIndexDto | null, key: string): number | null {
  const entry = index?.entries.find(e => e.key === key);
  if (entry == null || entry.conditionOrdinal === 0) {
    return null;
  }
  return entry.conditionOrdinal;
}

/**
 * The distinct condition ordinals the given runs and groups span, ascending, ignoring keys with
 * no assigned condition. A caller checks `length > 1` to know the selection crosses conditions.
 */
export function selectedConditions(
  index: BenchmarkComparabilityIndexDto | null,
  runIds: readonly number[],
  groupIds: readonly number[]
): number[] {
  const ordinals = new Set<number>();
  for (const runId of runIds) {
    const ordinal = conditionOf(index, `run:${runId}`);
    if (ordinal != null) {
      ordinals.add(ordinal);
    }
  }
  for (const groupId of groupIds) {
    const ordinal = conditionOf(index, `group:${groupId}`);
    if (ordinal != null) {
      ordinals.add(ordinal);
    }
  }
  return [...ordinals].sort((a, b) => a - b);
}

// ---------------------------------------------------------------------------------------------
// The step-1 notice band: what the current selection is about to cost, before Compare is asked
// ---------------------------------------------------------------------------------------------

/**
 * How much one notice about the selection matters.
 *
 * `error` — the selection cannot produce a figure at all. `warning` — it will produce one, over
 * less than what is ticked. `info` — something about the rendering is worth knowing before the
 * request goes out. The band gives each its own glyph, heading and colour, so no severity is ever
 * carried by hue alone.
 */
export type ComparisonNoticeSeverity = 'info' | 'warning' | 'error';

/** One fact about the current selection, ready for the band to render. */
export interface ComparisonSelectionNotice {
  /** Stable across recomputation: keys the `@for` track and lets a spec name one notice. */
  readonly id: string;
  readonly severity: ComparisonNoticeSeverity;
  /** The fact, in words. Severity is never carried by colour alone. */
  readonly heading: string;
  readonly body: string;
}

/** Everything the notice set is a function of. Nothing else about the wizard bears on it. */
export interface ComparisonSelectionState {
  readonly index: BenchmarkComparabilityIndexDto | null;
  readonly indexLoading: boolean;
  readonly indexError: string | null;
  readonly runIds: readonly number[];
  readonly groupIds: readonly number[];
  readonly pricingBasis: BenchmarkModelComparisonPricingBasis;
}

/** Render order: everything that blocks a figure, then everything that shrinks one, then the rest. */
const NOTICE_SEVERITY_ORDER: readonly ComparisonNoticeSeverity[] = ['error', 'warning', 'info'];

/**
 * The notices by descending severity, insertion order preserved inside each severity.
 *
 * Exported because two producers feed one band — the host's index-derived set and the wizard's own
 * plot-cap notice — and a rank written down twice is a rank that drifts.
 */
export function orderedNotices(
  notices: readonly ComparisonSelectionNotice[]
): ComparisonSelectionNotice[] {
  return [...notices].sort(
    (a, b) => NOTICE_SEVERITY_ORDER.indexOf(a.severity) - NOTICE_SEVERITY_ORDER.indexOf(b.severity)
  );
}

/** Every selected source as the index keys it, runs before groups. */
function selectionKeys(runIds: readonly number[], groupIds: readonly number[]): string[] {
  return [
    ...runIds.map(id => `run:${id}`),
    ...groupIds.map(id => `group:${id}`)
  ];
}

/** The index entries for a selection, skipping keys the index does not carry. */
function selectedEntries(
  index: BenchmarkComparabilityIndexDto | null,
  keys: readonly string[]
): BenchmarkComparabilityIndexEntryDto[] {
  const entries = index?.entries ?? [];
  return keys
    .map(key => entries.find(entry => entry.key === key))
    .filter((entry): entry is BenchmarkComparabilityIndexEntryDto => entry != null);
}

/** How an operator names one source in prose: the table it was ticked in, plus its id. */
function sourceLabel(entry: BenchmarkComparabilityIndexEntryDto): string {
  return entry.sourceKind === 'Group' ? `Analysis group ${entry.sourceId}` : `Run ${entry.sourceId}`;
}

/** The label of the condition everything else is compared against. */
function referenceLabel(index: BenchmarkComparabilityIndexDto | null): string {
  const conditions = index?.conditions ?? [];
  return conditions.find(condition => condition.ordinal === 1)?.label
    ?? conditions[0]?.label
    ?? 'the largest condition';
}

/**
 * Why part of a selection cannot be charted, or null when the selection sits in one condition.
 *
 * A private producer of {@link selectionNotices} rather than an exported helper: the picker owns
 * the checkboxes that produce the selection, the wizard's notice band is where the sentence has to
 * appear, and the host owns both, so the band is the single place the wording lives.
 */
function crossConditionNotice(state: ComparisonSelectionState): ComparisonSelectionNotice | null {
  if (selectedConditions(state.index, state.runIds, state.groupIds).length <= 1) {
    return null;
  }
  const keys = selectionKeys(state.runIds, state.groupIds);
  const excludedCount = keys.filter(key => conditionOf(state.index, key) !== 1).length;
  return {
    id: 'cross-condition',
    severity: 'warning',
    heading: 'Part of this selection will be excluded',
    body: `${excludedCount} of ${keys.length} selected sources fall outside `
      + `${referenceLabel(state.index)} and will be excluded from the comparison — only one `
      + 'condition can be charted.'
  };
}

/** A group whose own members disagree is not one point, whatever the rest of the selection is. */
function selfInconsistentNotice(state: ComparisonSelectionState): ComparisonSelectionNotice | null {
  const offenders = selectedEntries(state.index, selectionKeys(state.runIds, state.groupIds))
    .filter(entry => entry.selfInconsistent);
  if (offenders.length === 0) {
    return null;
  }
  const sentences = offenders.map(entry => {
    const keys = entry.selfInconsistentKeys.length > 0
      ? entry.selfInconsistentKeys.join(', ')
      : 'at least one comparability key';
    return `${sourceLabel(entry)} disagrees with itself on ${keys}.`;
  });
  return {
    id: 'self-inconsistent',
    severity: 'warning',
    heading: 'A selected group is not one point',
    body: `${sentences.join(' ')} Such a group is excluded whatever condition the rest of the `
      + 'selection is in.'
  };
}

/**
 * That the reference condition holds exactly one of the selected sources.
 *
 * The comparison itself succeeds; it is the figures that cannot be drawn, so this is a warning
 * about what step 3 will refuse rather than an error about the request.
 */
function singlePointNotice(state: ComparisonSelectionState): ComparisonSelectionNotice | null {
  const inReference = selectionKeys(state.runIds, state.groupIds)
    .filter(key => conditionOf(state.index, key) === 1);
  if (inReference.length !== 1) {
    return null;
  }
  return {
    id: 'single-point',
    severity: 'warning',
    heading: 'Only one point would be charted',
    body: 'A comparison needs two points; Compare will succeed but the figures will not be reachable.'
  };
}

/**
 * That a degrading key differs across the sources the figures would actually draw.
 *
 * The index carries both degrading values per entry precisely so the picker can say this before
 * Compare. The `AsRun` condition on the pricing snapshot mirrors the server's own rule: a set
 * repriced to one basis is not charting the stored snapshot prices, so a snapshot difference no
 * longer describes the cost axis.
 */
function degradingKeysNotice(state: ComparisonSelectionState): ComparisonSelectionNotice | null {
  const inReference = selectedEntries(state.index, selectionKeys(state.runIds, state.groupIds))
    .filter(entry => entry.conditionOrdinal === 1);

  const sentences: string[] = [];
  const parallelismDiffers = new Set(inReference.map(entry => entry.questionParallelism)).size > 1;
  if (parallelismDiffers) {
    sentences.push('QuestionParallelism differs across them, which flags the speed axis and the '
      + 'cost axis: running questions concurrently changes prompt-cache behaviour as well as timing.');
  }
  if (state.pricingBasis === 'AsRun'
    && new Set(inReference.map(entry => entry.pricingSnapshot)).size > 1) {
    sentences.push('PricingSnapshot differs, which flags the cost axis while costs are charted as '
      + 'they were run.');
  }
  if (sentences.length === 0) {
    return null;
  }
  return {
    id: 'degrading-keys',
    severity: 'warning',
    // The heading names the axes actually at stake: question parallelism degrades speed and cost
    // alike, the pricing snapshot degrades cost only.
    heading: parallelismDiffers
      ? 'The speed and cost axes will be flagged'
      : 'The cost axis will be flagged',
    body: `${sentences.join(' ')} A flagged axis is still charted, with the notice that names the `
      + 'key on the figure itself.'
  };
}

/**
 * Everything worth saying about the current selection, ordered error → warning → info.
 *
 * Pure, and the only place any of this wording lives: the picker owns the checkboxes, the wizard
 * owns the band, and the host owns both, so neither view may hold its own copy of a sentence.
 */
export function selectionNotices(state: ComparisonSelectionState): ComparisonSelectionNotice[] {
  const notices: ComparisonSelectionNotice[] = [];
  const keys = selectionKeys(state.runIds, state.groupIds);

  const indexError = state.indexError?.trim() ?? '';
  if (indexError.length > 0) {
    notices.push({
      id: 'index-error',
      severity: 'error',
      heading: 'Conditions could not be computed',
      body: `${indexError} Every row's Condition reads —, so a selection made now cannot be `
        + 'checked for comparability before Compare.'
    });
  }

  // Only once the index has landed: an empty condition set is otherwise just the state before the
  // round trip returns, which `index-loading` already reports.
  if (keys.length > 0
    && state.index != null
    && selectedConditions(state.index, state.runIds, state.groupIds).length === 0) {
    notices.push({
      id: 'no-condition',
      severity: 'error',
      heading: 'Nothing in this selection can be charted',
      body: 'Every selected source is either self-inconsistent or has no runs, so no condition '
        + 'contains any of them and the comparison would chart nothing.'
    });
  }

  const warnings = [
    crossConditionNotice(state),
    selfInconsistentNotice(state),
    singlePointNotice(state),
    degradingKeysNotice(state)
  ];
  for (const notice of warnings) {
    if (notice != null) {
      notices.push(notice);
    }
  }

  if (state.indexLoading) {
    notices.push({
      id: 'index-loading',
      severity: 'info',
      heading: 'Conditions are still being computed',
      body: 'The Condition column and both condition filters stay muted until the index lands.'
    });
  }

  return orderedNotices(notices);
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
