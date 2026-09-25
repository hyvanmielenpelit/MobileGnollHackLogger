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
import type { FigureNote } from './figure-chrome';
import { modelLabelText } from './model-comparison-charts';
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
  /** Items behind the estimate: questions with a scored answer. */
  itemCount: number;
  /** Questions these runs were asked: `itemCount + unscoredItemCount`. */
  examItemCount: number;
  /** Questions asked with no answer that counts: failed, skipped, canceled or ungraded. */
  unscoredItemCount: number;
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
  /** Mean model time per question, ms — turn duration with tool I/O removed, pooled across the run(s). */
  modelTimeMeanMs?: number | null;
  /** Mean of the per-run total model time for the whole suite, ms. */
  totalModelTimePerRunMeanMs?: number | null;
  /** SD of the per-run total model time across runs, ms. Null below R = 2, where no run-to-run spread exists. */
  totalModelTimeSdMs?: number | null;
}

/**
 * The cost axis: candidate spend per question asked, in USD.
 *
 * Candidate-only by design — grading-role spend is most of a run's cost and none of it transfers to
 * the chat assistant — which is why no field here totals a run including its graders.
 */
export interface BenchmarkModelComparisonCostDto {
  /** Null unless a price card resolved for every run behind the entry; an unknown cost is never zero. */
  candidateCostPerQuestionUsd?: number | null;
  candidateCostPerRunUsd?: number | null;
  candidateTotalCostUsd?: number | null;
  /** Questions each run behind the entry asked, averaged: the denominator of the per-question cost. */
  questionsAskedPerRun?: number | null;
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

  /** One plain-language sentence: what the catch is, for a reader who will not follow the reason. */
  summary: string;
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
  /** Degrading key value: the speed calibration the Speed Index was computed under. */
  speedCalibration: string;
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

/** One selected source as the wizard's selection band names it. Built by the host from its option lists. */
export interface ComparisonSelectedSource {
  readonly kind: 'run' | 'group';
  readonly id: number;
  /** Run: the tested model's display name. Group: the group's name. */
  readonly label: string;
  /** Run: the tested model's provider. Groups carry none. */
  readonly provider: string | null;
  /** Run: "#48". Group: "3 runs". */
  readonly detail: string;
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

/**
 * How an operator names one source in prose: the table it was ticked in, plus its id.
 *
 * The parameter is the pair of fields the label reads rather than one DTO, so the comparability
 * index entry and the comparison entry both satisfy it.
 */
export function sourceLabel(entry: { sourceKind: string; sourceId: number }): string {
  return entry.sourceKind === 'Group' ? `Analysis group ${entry.sourceId}` : `Run ${entry.sourceId}`;
}

const ISO_DATE_SEGMENT = /^\d{4}-\d{2}-\d{2}$/;
const RUN_COUNT_SEGMENT = /^R=\d+$/;

/**
 * Whether an analysis group's name only repeats facts its row already shows, so it can be left
 * unrendered: every `' · '`-separated segment is the suite name, the model name, an ISO date or a
 * run count. That covers both generators — `BenchmarkSeriesOrchestrator`'s
 * `Suite · Model · yyyy-MM-dd · R=n` and `defaultGroupName` in `benchmark.component.ts`,
 * `Suite · R=n` — and a third generator must produce the same segments or its names are shown.
 * Anything a user typed fails the test.
 */
export function isGeneratedGroupName(
  name: string,
  suiteName: string | null | undefined,
  modelName: string | null | undefined
): boolean {
  const trimmed = name.trim();
  if (trimmed === '') {
    return false;
  }
  const suite = suiteName?.trim() ?? '';
  const model = modelName?.trim() ?? '';
  return trimmed.split(' · ').every(part => {
    const segment = part.trim();
    return segment !== ''
      && ((suite !== '' && segment === suite)
        || (model !== '' && segment === model)
        || ISO_DATE_SEGMENT.test(segment)
        || RUN_COUNT_SEGMENT.test(segment));
  });
}

/** The condition everything else is compared against: ordinal 1, or the first one on offer. */
function referenceConditionOf(
  index: BenchmarkComparabilityIndexDto | null
): BenchmarkComparabilityConditionDto | null {
  const conditions = index?.conditions ?? [];
  return conditions.find(condition => condition.ordinal === 1) ?? conditions[0] ?? null;
}

/** The label of the condition everything else is compared against. */
function referenceLabel(index: BenchmarkComparabilityIndexDto | null): string {
  return referenceConditionOf(index)?.label ?? 'the largest condition';
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
 * The comparison itself succeeds; it is the charts that cannot be drawn, so this is a warning
 * about the chart views step 3 will refuse rather than an error about the request.
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
 * The index carries every degrading value per entry precisely so the picker can say this before
 * Compare. The `AsRun` condition on the pricing snapshot mirrors the server's own rule: a set
 * repriced to one basis is not charting the stored snapshot prices, so a snapshot difference no
 * longer describes the cost axis.
 */
function degradingKeysNotice(state: ComparisonSelectionState): ComparisonSelectionNotice | null {
  const inReference = selectedEntries(state.index, selectionKeys(state.runIds, state.groupIds))
    .filter(entry => entry.conditionOrdinal === 1);

  const sentences: string[] = [];
  let speedAtStake = false;
  let costAtStake = false;

  if (new Set(inReference.map(entry => entry.questionParallelism)).size > 1) {
    speedAtStake = true;
    costAtStake = true;
    sentences.push('QuestionParallelism differs across them, which flags the speed axis and the '
      + 'cost axis: running questions concurrently changes prompt-cache behaviour as well as timing.');
  }
  if (new Set(inReference.map(entry => entry.speedCalibration)).size > 1) {
    speedAtStake = true;
    sentences.push('SpeedCalibration differs, which flags the speed axis: the Speed Index of these '
      + 'runs was computed against different targets, so their indices are not on one scale. The '
      + 'model time measures are unaffected — rescore a run to move it onto the current scale.');
  }
  if (state.pricingBasis === 'AsRun'
    && new Set(inReference.map(entry => entry.pricingSnapshot)).size > 1) {
    costAtStake = true;
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
    // alike, the speed calibration degrades speed only, the pricing snapshot cost only.
    heading: speedAtStake && costAtStake
      ? 'The speed and cost axes will be flagged'
      : speedAtStake
        ? 'The speed axis will be flagged'
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
// The condition detail: key by key, how one source differs from the reference condition
// ---------------------------------------------------------------------------------------------

/** One `name=value` field of a configuration-shaped comparability value. */
export interface ConfigurationField {
  readonly name: string;
  readonly value: string;
}

/**
 * A `key=value;key=value` value split into its fields in the order it carries them, or null when
 * the value is not of that shape.
 *
 * `AssessorConfiguration` and its siblings are written this way, and a reader holding two of them
 * side by side wants the one field that moved, not two forty-character strings to diff by eye.
 * Everything else the index carries — a digest, a serialized options object, a comma-separated
 * list — has no fields, and null is how the caller learns to render the value whole instead.
 *
 * A field value of `(none)` stays verbatim: it is what the wire writes for a setting that is not
 * configured, and a field set on one side and absent on the other is precisely the difference.
 */
export function parseConfigurationValue(value: string): ConfigurationField[] | null {
  const text = (value ?? '').trim();
  if (text === '') {
    return null;
  }
  const fields: ConfigurationField[] = [];
  for (const part of text.split(';')) {
    const segment = part.trim();
    if (segment === '') {
      continue;
    }
    const separator = segment.indexOf('=');
    const name = separator < 0 ? '' : segment.slice(0, separator).trim();
    // One malformed field disqualifies the whole value: a half-parsed configuration would show
    // fields that are real beside a remainder that silently vanished.
    if (name === '' || !/^[A-Za-z][A-Za-z0-9_.-]*$/.test(name)) {
      return null;
    }
    fields.push({ name, value: segment.slice(separator + 1).trim() });
  }
  return fields.length > 0 ? fields : null;
}

/** One value of a differing key, with the runs that carried it. */
export interface ConditionDetailVariant {
  readonly value: string;
  readonly runIds: readonly number[];
}

/** One key a source differs from the reference condition on, with both sides of the difference. */
export interface ConditionDetailRow {
  readonly name: string;
  /** The human label the reference condition describes the key by, falling back to its name. */
  readonly label: string;
  /** `Fundamental`, `Candidate`, `Instrument` or `SpeedAndCost`, as the server spells them. */
  readonly kind: string;
  /** `Text`, `Identifier`, `Hash`, `Json` or `List` — what shape the values are, not what they mean. */
  readonly valueKind: string;
  /** One line on what a difference costs a comparison, as the reference condition describes the key. */
  readonly description: string;
  /** This source's value, or null when no variant could be attributed to its runs. */
  readonly thisValue: string | null;
  /** The reference condition's value, or null when no variant could be attributed to it. */
  readonly referenceValue: string | null;
  readonly thisFields: ConfigurationField[] | null;
  readonly referenceFields: ConfigurationField[] | null;
  /** Field names that differ between the two parsed sets, including those present on one side only. */
  readonly changedFields: string[];
  /** Every variant with its runs, populated only when a side could not be attributed. */
  readonly variants: ConditionDetailVariant[];
}

/** Everything one source's comparability detail says, ready to render and to paste. */
export interface ConditionDetail {
  readonly key: string;
  /** `Run` or `Group`. */
  readonly sourceKind: string;
  readonly sourceId: number;
  /** How an operator names the source in prose: "Run 46", "Analysis group 3". */
  readonly sourceLabel: string;
  readonly conditionLabel: string;
  readonly conditionOrdinal: number;
  readonly referenceConditionLabel: string;
  readonly selfInconsistent: boolean;
  readonly selfInconsistentKeys: string[];
  readonly rows: ConditionDetailRow[];
}

/** The field names two parsed configurations disagree on, this side's order first. */
function changedFieldNames(
  thisFields: ConfigurationField[] | null,
  referenceFields: ConfigurationField[] | null
): string[] {
  if (thisFields == null || referenceFields == null) {
    return [];
  }
  const reference = new Map(referenceFields.map(field => [field.name, field.value] as const));
  const changed = thisFields
    .filter(field => reference.get(field.name) !== field.value)
    .map(field => field.name);
  for (const field of referenceFields) {
    if (!thisFields.some(other => other.name === field.name)) {
      changed.push(field.name);
    }
  }
  return changed;
}

/**
 * Which variant is this source's and which is the reference condition's.
 *
 * Two independent facts are available and neither is always present. A variant's `runIds` names
 * this source when the caller knows its runs — which it does for a single run and may not for a
 * group, whose membership the picker's list endpoint does not carry. The reference condition's own
 * value for the key is in `largestConditionKeys` whenever the key is described there. Either alone
 * settles a two-variant difference; with neither, both sides stay null and the caller lists every
 * variant rather than guessing.
 */
function attributeVariants(
  variants: readonly ConditionDetailVariant[],
  memberRunIds: readonly number[],
  referenceKeyValue: string | null
): { mine: ConditionDetailVariant | null; reference: ConditionDetailVariant | null } {
  let mine = variants.find(
    variant => variant.runIds.some(id => memberRunIds.includes(id))) ?? null;
  let reference = referenceKeyValue == null
    ? null
    : variants.find(variant => variant !== mine && variant.value === referenceKeyValue) ?? null;

  // A two-variant difference is settled by either fact alone: whichever side is known, the other
  // is the remaining variant. Three or more variants are left unattributed rather than guessed at.
  if (variants.length === 2) {
    if (mine == null && reference != null) {
      mine = variants.find(variant => variant !== reference) ?? null;
    } else if (reference == null && mine != null) {
      reference = variants.find(variant => variant !== mine) ?? null;
    }
  }
  return { mine, reference };
}

/**
 * One source's comparability detail, or null when the index has not loaded or lacks the key.
 *
 * `memberRunIds` is the source's own runs — one id for a run, the group's membership for a group —
 * and is used only to tell this source's value of a differing key from the reference condition's.
 * An empty list is legitimate and costs the attribution, not the row.
 */
export function conditionDetailFor(
  index: BenchmarkComparabilityIndexDto | null,
  key: string,
  memberRunIds: readonly number[]
): ConditionDetail | null {
  const entry = index?.entries.find(candidate => candidate.key === key);
  if (index == null || entry == null) {
    return null;
  }

  const rows = entry.differencesFromLargest.map(difference => {
    const described = index.largestConditionKeys
      .find(referenceKey => referenceKey.name === difference.name);
    const variants: ConditionDetailVariant[] = difference.variants
      .map(variant => ({ value: variant.value, runIds: [...variant.runIds] }));
    const { mine, reference } = attributeVariants(variants, memberRunIds, described?.value ?? null);

    const thisFields = mine == null ? null : parseConfigurationValue(mine.value);
    const referenceFields = reference == null ? null : parseConfigurationValue(reference.value);
    return {
      name: difference.name,
      label: described?.label || difference.name,
      kind: described?.kind || difference.kind,
      valueKind: described?.valueKind || 'Text',
      description: described?.description || difference.description,
      thisValue: mine?.value ?? null,
      referenceValue: reference?.value ?? null,
      thisFields,
      referenceFields,
      changedFields: changedFieldNames(thisFields, referenceFields),
      // Listed whole only where the two sides could not be told apart, so the reader is given the
      // evidence rather than an attribution the data does not support.
      variants: mine == null || reference == null ? variants : []
    };
  });

  return {
    key: entry.key,
    sourceKind: entry.sourceKind,
    sourceId: entry.sourceId,
    sourceLabel: sourceLabel(entry),
    conditionLabel: entry.conditionLabel,
    conditionOrdinal: entry.conditionOrdinal,
    referenceConditionLabel: referenceLabel(index),
    selfInconsistent: entry.selfInconsistent,
    selfInconsistentKeys: [...entry.selfInconsistentKeys],
    rows
  };
}

// ---------------------------------------------------------------------------------------------
// The condition legend: every condition of the index, the charted one first
// ---------------------------------------------------------------------------------------------

/** Hex characters of a digest that stay legible: git's own abbreviation, and enough to cite. */
export const SHORT_HASH_LENGTH = 12;

/** A digest-shaped value, abbreviated on screen whatever kind the index declares for its key. */
export const HASH_SHAPED = /^[0-9a-fA-F]{32,}$/;

/** Characters past which a differing value is too long to show inline beside its counterpart. */
const INLINE_DIFFERENCE_CHARS = 80;

/** One condition as the legend lists it. */
export interface ConditionLegendItem {
  readonly condition: BenchmarkComparabilityConditionDto;
  readonly isReference: boolean;
  /** Entry keys of the sources in this condition, runs first then groups, each by id ascending. */
  readonly memberKeys: readonly string[];
  /** A member used to read the cohort's differences: a run where one exists, else the first group. */
  readonly representativeKey: string | null;
  /** Labels of the keys the cohort differs from the reference on; empty for the reference. */
  readonly differingKeyLabels: readonly string[];
}

/** Every condition of one index, ready for the legend, and the entry lookup built on the way. */
export interface ConditionLegend {
  readonly reference: ConditionLegendItem | null;
  /** Every other condition, newest run first; a missing date sorts last; ties by ordinal. */
  readonly others: readonly ConditionLegendItem[];
  /** Entry lookup by key, built once — the picker's tables use it too. */
  readonly entryByKey: ReadonlyMap<string, BenchmarkComparabilityIndexEntryDto>;
}

/** A start time as a sortable number, or null when it is absent or does not parse. */
function startedAtMs(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

/**
 * The legend's view of one index, built in one pass over its entries.
 *
 * Every source in a condition shares one must-match signature, so any single member's
 * `differencesFromLargest` describes the whole cohort — which is why no per-condition difference
 * list has to be sent. A run is preferred as that member because its own run id attributes each
 * differing value to a side, where a group's membership may not have been sent.
 */
export function buildConditionLegend(index: BenchmarkComparabilityIndexDto | null): ConditionLegend {
  const entryByKey = new Map<string, BenchmarkComparabilityIndexEntryDto>();
  const membersByOrdinal = new Map<number, BenchmarkComparabilityIndexEntryDto[]>();
  for (const entry of index?.entries ?? []) {
    entryByKey.set(entry.key, entry);
    if (entry.conditionOrdinal === 0) {
      continue;
    }
    const members = membersByOrdinal.get(entry.conditionOrdinal);
    if (members == null) {
      membersByOrdinal.set(entry.conditionOrdinal, [entry]);
    } else {
      members.push(entry);
    }
  }
  if (index == null) {
    return { reference: null, others: [], entryByKey };
  }

  const labelOf = new Map(index.largestConditionKeys.map(key => [key.name, key.label] as const));
  const referenceCondition = referenceConditionOf(index);

  const item = (condition: BenchmarkComparabilityConditionDto): ConditionLegendItem => {
    const members = [...(membersByOrdinal.get(condition.ordinal) ?? [])].sort((a, b) => {
      const kindA = a.sourceKind === 'Group' ? 1 : 0;
      const kindB = b.sourceKind === 'Group' ? 1 : 0;
      return kindA - kindB || a.sourceId - b.sourceId;
    });
    const representative = members[0] ?? null;
    const isReference = condition === referenceCondition;
    return {
      condition,
      isReference,
      memberKeys: members.map(member => member.key),
      representativeKey: representative?.key ?? null,
      differingKeyLabels: isReference
        ? []
        : (representative?.differencesFromLargest ?? [])
          .map(difference => labelOf.get(difference.name) || difference.name)
    };
  };

  const others = index.conditions
    .filter(condition => condition !== referenceCondition)
    .map(item)
    .sort((a, b) => {
      const timeA = startedAtMs(a.condition.newestRunStartedAtUtc);
      const timeB = startedAtMs(b.condition.newestRunStartedAtUtc);
      if (timeA !== timeB) {
        if (timeA == null) {
          return 1;
        }
        if (timeB == null) {
          return -1;
        }
        return timeB - timeA;
      }
      return a.condition.ordinal - b.condition.ordinal;
    });

  return {
    reference: referenceCondition == null ? null : item(referenceCondition),
    others,
    entryByKey
  };
}

/**
 * One differing key reduced to what a reader scans: "charted → this".
 *
 * `from` is always the reference (charted) condition's value and `to` this condition's, so every
 * line reads in one direction.
 */
export type ConditionDifferenceSummary =
  | { readonly kind: 'fields'; readonly changes: readonly { name: string; from: string; to: string }[] }
  /** Both sides already shortened to {@link SHORT_HASH_LENGTH}. */
  | { readonly kind: 'hash'; readonly from: string; readonly to: string }
  | { readonly kind: 'text'; readonly from: string; readonly to: string }
  /** At least one side is too long to show inline. */
  | { readonly kind: 'long' }
  /** The sides could not be told apart. */
  | { readonly kind: 'unattributed' };

/** A value short and single-line enough to sit inline beside its counterpart. */
function fitsInline(value: string): boolean {
  return value.length <= INLINE_DIFFERENCE_CHARS && !/[\r\n]/.test(value);
}

export function summarizeConditionDifference(row: ConditionDetailRow): ConditionDifferenceSummary {
  const from = row.referenceValue;
  const to = row.thisValue;
  if (from == null || to == null) {
    return { kind: 'unattributed' };
  }

  if (row.referenceFields != null && row.thisFields != null && row.changedFields.length > 0) {
    const fieldValue = (fields: readonly ConfigurationField[], name: string): string =>
      fields.find(field => field.name === name)?.value ?? '(none)';
    return {
      kind: 'fields',
      changes: row.changedFields.map(name => ({
        name,
        from: fieldValue(row.referenceFields!, name),
        to: fieldValue(row.thisFields!, name)
      }))
    };
  }

  if (row.valueKind === 'Hash' || (HASH_SHAPED.test(from.trim()) && HASH_SHAPED.test(to.trim()))) {
    return {
      kind: 'hash',
      from: from.trim().slice(0, SHORT_HASH_LENGTH),
      to: to.trim().slice(0, SHORT_HASH_LENGTH)
    };
  }

  if (!fitsInline(from) || !fitsInline(to)) {
    return { kind: 'long' };
  }
  return { kind: 'text', from, to };
}

// ---------------------------------------------------------------------------------------------
// The adapter onto the chart core's input shape
// ---------------------------------------------------------------------------------------------

/**
 * Three fields the chart core declares that this endpoint does not carry, named once here so the
 * view can say so out loud rather than each caller rediscovering it. `modelTimeMeanMs`,
 * `totalModelTimeMs` and `totalModelTimeSdMs` are deliberately not in this set — the DTO does carry
 * them, and {@link toChartEntries} maps them below like every other measured field:
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
 * A stored thinking level as chart labels print it: trimmed, first letter lower-cased, the rest
 * verbatim. Null when there is none.
 */
export function normalizeThinkingLevel(level: string | null | undefined): string | null {
  const trimmed = level?.trim() ?? '';
  return trimmed.length === 0 ? null : trimmed.charAt(0).toLowerCase() + trimmed.slice(1);
}

/**
 * An entry's chart label, always carrying its thinking level: `GPT-5.6 Luna (max)`. The server's
 * `label` appends the level only when the set mixes levels, so it is not used while the entry
 * carries a display name.
 */
function chartLabel(entry: BenchmarkModelComparisonEntryDto): Pick<ModelComparisonEntry, 'label' | 'name' | 'thinkingLevel'> {
  const name = entry.modelDisplayName || entry.modelId || entry.label;
  const thinkingLevel = normalizeThinkingLevel(entry.thinkingLevel);
  return { label: modelLabelText(name, thinkingLevel), name, thinkingLevel };
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
    ...chartLabel(entry),
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

    modelTimeMeanMs: entry.speed?.modelTimeMeanMs ?? UNMEASURED,
    totalModelTimeMs: entry.speed?.totalModelTimePerRunMeanMs ?? UNMEASURED,
    totalModelTimeSdMs: entry.speed?.totalModelTimeSdMs ?? null,

    candidateCostPerQuestionUsd: entry.cost?.candidateCostPerQuestionUsd ?? UNMEASURED,
    candidateCostPerQuestionSdUsd: null,
    candidateCostPerRunUsd: entry.cost?.candidateCostPerRunUsd ?? UNMEASURED,
    totalRunCostUsd: UNMEASURED,
    totalRunCostSdUsd: null,

    speedDegraded: entry.speedDegraded,
    costDegraded: entry.costDegraded,
    excluded: entry.excluded,
    excludedReasonKeys: entry.excludingKeys,
  }));
}

/**
 * The set-level facts the figures put in their chrome.
 *
 * `scoredItemsMin` and `scoredItemsMax` span the charted entries' scored item counts, which can
 * differ: an entry's failed, skipped or ungraded answers leave questions out of its index alone.
 * `examItemCount` is shared, because the suite is a Fundamental key; the max tolerates a payload
 * without the field, which reads as 0 (unknown). `questionsAskedPerRun` is null when the charted
 * entries asked different numbers of questions, or none reported it.
 */
export function toChartContext(dto: BenchmarkModelComparisonDto | null): ModelComparisonContext {
  const charted = dto?.entries.filter(entry => !entry.excluded && entry.quality != null) ?? [];
  const scored = charted.map(entry => entry.quality?.itemCount ?? 0);
  const asked = charted
    .map(entry => entry.cost?.questionsAskedPerRun)
    .filter((n): n is number => n != null);
  return {
    scoredItemsMin: scored.length > 0 ? Math.min(...scored) : 0,
    scoredItemsMax: scored.length > 0 ? Math.max(...scored) : 0,
    examItemCount: charted.reduce((max, entry) => Math.max(max, entry.quality?.examItemCount ?? 0), 0),
    questionsAskedPerRun: asked.length > 0 && asked.every(n => Math.abs(n - asked[0]) <= 1e-9) ? asked[0] : null,
    pricingBasisLabel: dto?.pricingBasisLabel || dto?.pricingBasis || 'Unknown pricing basis',
    pricingBasis: dto?.pricingBasis ?? '',
    pricedOn: dto?.computedAtUtc ?? '',
    suiteName: dto?.baselineSuiteName ?? '',
  };
}

/**
 * Why the charted entries' indices cover fewer questions than these runs were asked. An entry's
 * own unscored questions are a warning each, because indices over different item sets are not
 * strictly the same exam. Empty when every asked question is scored.
 */
export function questionCoverageNotes(entries: readonly BenchmarkModelComparisonEntryDto[]): FigureNote[] {
  const notes: FigureNote[] = [];

  for (const entry of entries) {
    const unscored = entry.quality?.unscoredItemCount ?? 0;
    if (unscored <= 0) {
      continue;
    }
    const one = unscored === 1;
    notes.push({
      text: `${chartLabel(entry).label}: ${unscored} ` +
        `${one ? 'question has' : 'questions have'} no scored answer (failed, skipped or ungraded) and ` +
        `${one ? 'is' : 'are'} left out of its index.`,
      tone: 'warning',
    });
  }

  return notes;
}
