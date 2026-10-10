/**
 * What one Results section's image says, as typed blocks: headings, paragraphs, fact lists, chip rows,
 * key figures, and the endpoint, period, attribution and next-run cards. Built from the stored result
 * with the same functions the tabs render from, so the image says what the tab says.
 *
 * Pure. Each section lists its items ({@link ccResultsImageItems}); a block or a part of a card is drawn
 * only while `include` accepts its item key. No text in a block carries a hash: {@link ccImageText}
 * removes instrument fingerprints and other long hexadecimal tokens from server text.
 */

import { formatServiceTier, formatThinkingLevel } from '../../../../utils/model-badge-format.util';
import type { ImageBadgeTone, ImageFactRow } from '../../run-report-frame/key-figures-image';
import { CC_CHECK_STATUS_TEXT, CC_EP_VISIBLE_NOTES, CcIntervalEnds, intervalEnds } from '../analysis-wizard/endpoint-card/endpoint-card.component';
import { ccNextRunSections, ccNextRunsLeadText } from '../analysis-wizard/next-runs/next-runs.component';
import { CcEventDay, eventGroupChangesText } from '../chat-consistency-events';
import {
  NO_VALUE,
  annotationKindText,
  endpointMdeText,
  formatInteger,
  formatUtcDate,
  formatUtcDateTime,
  gradeText,
  plural,
  verdictText
} from '../chat-consistency-format';
import {
  CcEndpointStatus,
  CcIntervalGeometry,
  CcResultKeyFigure,
  ccEndpointNotes,
  ccEndpointStatus,
  ccEndpointStatusText,
  ccEstimateParts,
  ccIntervalGeometry,
  ccModelBaseName,
  ccNextRunActionCount,
  ccNextRunGroups,
  ccNotComputableGroups,
  ccOverallOutcome,
  ccResultKeyFigures,
  ccResultPeriodUnits
} from '../chat-consistency-results';
import {
  CcAnalysisResult,
  CcAttributionResult,
  CcBatteryRunRow,
  CcEndpointResult,
  CcPeriodSummary,
  CcRunRow,
  CcRunSelectionView,
  CcUnanalyzedReason,
  CcVerdict
} from '../chat-consistency.models';
import { CC_RESULTS_IMAGE_SECTION_LABELS, CcResultsImageSection } from './results-image-settings';

// -----------------------------------------------------------------------------------------------
// Shared readings of the result
// -----------------------------------------------------------------------------------------------

/** The attribution groups, in the order the results show them. */
export const CC_ATTRIBUTION_GROUPS: readonly { readonly side: string; readonly title: string }[] = [
  { side: 'ours', title: 'Our changes' },
  { side: 'provider', title: 'Provider' },
  { side: 'infrastructure', title: 'Infrastructure' },
  { side: 'undetermined', title: 'Undetermined' }
];

/** Why a usable run in the periods was not analyzed, in the order the server classifies it. */
export const CC_UNANALYZED_REASONS: readonly { readonly reason: CcUnanalyzedReason; readonly label: string }[] = [
  { reason: 'leftOut', label: 'Left out in step 1' },
  { reason: 'outsideDateRange', label: 'Outside the step-1 dates' },
  { reason: 'beforeFirstRun', label: 'Before the first run' },
  { reason: 'afterLastRun', label: 'After the last run' },
  { reason: 'notSelected', label: 'Not assigned to a period' },
  { reason: 'outsideComparisonSet', label: 'Outside the compared set' }
];

/** A decisive change the attribution explains. */
export interface CcDecisiveChange {
  id: string;
  name: string;
  verdict: CcVerdict;
  verdictText: string;
}

/** The attributions of one side. */
export interface CcAttributionGroupView {
  side: string;
  title: string;
  attributions: CcAttributionResult[];
}

/** The Attribution tab's reading of a result. */
export interface CcAttributionView {
  decisiveChanges: CcDecisiveChange[];
  /** The sides with attributions, in {@link CC_ATTRIBUTION_GROUPS} order. */
  groups: CcAttributionGroupView[];
  /** Nothing decisive and nothing attributed beyond *Undetermined*. */
  empty: boolean;
  /** `Nothing is attributed to infrastructure.`; empty when every side has an attribution. */
  unattributedText: string;
}

/** The order of the computed endpoint cards: changes first, inconclusive last. */
const STATUS_ORDER: Readonly<Record<CcEndpointStatus, number>> = {
  changed: 0,
  improved: 1,
  within: 2,
  inconclusive: 3,
  notComputable: 4
};

const DECISIVE_VERDICTS: readonly CcVerdict[] = ['changedDegraded', 'changedImproved'];

/** The sides an attribution names, as the line about the sides without one reads them. */
const ATTRIBUTED_SIDE_NAMES: readonly { readonly side: string; readonly name: string }[] = [
  { side: 'ours', name: 'our changes' },
  { side: 'provider', name: 'the provider' },
  { side: 'infrastructure', name: 'infrastructure' }
];

/** `a`, `a or b`, `a, b or c`. */
function orList(items: readonly string[]): string {
  if (items.length <= 1) return items.join('');
  return `${items.slice(0, -1).join(', ')} or ${items[items.length - 1]}`;
}

/** The computed endpoints: changed, improved, within margin, inconclusive, then by id. */
export function ccComputedEndpoints(result: CcAnalysisResult): CcEndpointResult[] {
  return result.endpoints
    .filter(endpoint => endpoint.computed)
    .map(endpoint => ({ endpoint, rank: STATUS_ORDER[ccEndpointStatus(endpoint)] }))
    .sort((a, b) => a.rank - b.rank || a.endpoint.id.localeCompare(b.endpoint.id, undefined, { numeric: true }))
    .map(entry => entry.endpoint);
}

/** The decisive changes, the attributions by side, and the sides without one. */
export function ccAttributionView(result: CcAnalysisResult): CcAttributionView {
  const byId = new Map(result.endpoints.map(endpoint => [endpoint.id, endpoint] as const));
  const decisive: CcDecisiveChange[] = [];
  for (const change of result.attribution.totalChanges) {
    const endpoint = byId.get(change.endpointId);
    if (!endpoint?.verdict || !DECISIVE_VERDICTS.includes(endpoint.verdict)) continue;
    if (decisive.some(entry => entry.id === endpoint.id)) continue;
    decisive.push({
      id: endpoint.id,
      name: change.name || endpoint.name,
      verdict: endpoint.verdict,
      verdictText: verdictText(endpoint.verdict, endpoint.verdictLabel)
    });
  }
  const attributionsOf = (side: string) => result.attribution.attributions.filter(attribution => attribution.side === side);
  const groups = CC_ATTRIBUTION_GROUPS
    .map(group => ({ side: group.side, title: group.title, attributions: attributionsOf(group.side) }))
    .filter(group => group.attributions.length > 0);
  const attributed = result.attribution.attributions.some(attribution => attribution.side !== 'undetermined');
  const missing = ATTRIBUTED_SIDE_NAMES
    .filter(entry => !groups.some(group => group.side === entry.side))
    .map(entry => entry.name);
  return {
    decisiveChanges: decisive,
    groups,
    empty: decisive.length === 0 && !attributed,
    unattributedText: missing.length > 0 ? `Nothing is attributed to ${orList(missing)}.` : ''
  };
}

/** The recorded run selection; null for an analysis saved before it was recorded, with nothing to show. */
export function ccShownRunSelection(result: CcAnalysisResult): CcRunSelectionView | null {
  const selection = result.runSelection;
  return selection && (selection.recorded || selection.unanalyzedRuns.length > 0) ? selection : null;
}

/** `#102`, or `battery run #12` in a battery analysis; `none` without a mark. */
export function ccRunMark(result: CcAnalysisResult, runId: number | null | undefined): string {
  if (runId === null || runId === undefined) return 'none';
  return result.unitKind === 'batteryRun' ? `battery run #${runId}` : `#${runId}`;
}

/** `2026-09-01 00:00 UTC to the last run`, the UTC bounds of the step-1 dates; empty when both are open. */
export function ccRangeBoundsText(selection: CcRunSelectionView): string {
  if (!selection.rangeFromUtc && !selection.rangeToUtc) return '';
  const from = selection.rangeFromUtc ? formatUtcDateTime(selection.rangeFromUtc) : 'the first run';
  const to = selection.rangeToUtc ? formatUtcDateTime(selection.rangeToUtc) : 'the last run';
  return `${from} to ${to}`;
}

/** The run selection's marks: *First run*, *Last run* and *Left out in step 1*. */
export function ccRunSelectionMarks(result: CcAnalysisResult, selection: CcRunSelectionView): { first: string; last: string; leftOut: string } {
  const battery = result.unitKind === 'batteryRun';
  const leftOut = battery ? selection.leftOutBatteryRunIds ?? [] : selection.leftOutRunIds;
  return {
    first: ccRunMark(result, battery ? selection.firstBatteryRunId : selection.firstRunId),
    last: ccRunMark(result, battery ? selection.lastBatteryRunId : selection.lastRunId),
    leftOut: leftOut.length > 0 ? leftOut.map(id => ccRunMark(result, id)).join(', ') : 'none'
  };
}

/**
 * The unanalyzed runs by reason, in the server's reason order: `#45 (baseline), #51 (comparison)`,
 * with the battery run a reason applies to: `#98 (baseline, battery run #12)`.
 */
export function ccUnanalyzedGroups(selection: CcRunSelectionView): { reason: string; label: string; runs: string }[] {
  return CC_UNANALYZED_REASONS
    .map(({ reason, label }) => ({
      reason,
      label,
      runs: selection.unanalyzedRuns
        .filter(run => run.reason === reason)
        .map(run => run.batteryRunId !== null && run.batteryRunId !== undefined
          ? `#${run.runId} (${run.period}, battery run #${run.batteryRunId})`
          : `#${run.runId} (${run.period})`)
        .join(', ')
    }))
    .filter(group => group.runs !== '');
}

// -----------------------------------------------------------------------------------------------
// Text without hashes
// -----------------------------------------------------------------------------------------------

/** ` (instrument 0123456789ab)`, as the server's control suggestions end. */
const INSTRUMENT_TEXT = /\s*\(instrument\s+[0-9a-f]{6,}\)/gi;

/** A hexadecimal token of 12 or more characters with at least one letter: a hash or a fingerprint, never a count. */
const HEX_TOKEN = /\b(?=[0-9a-f]*[a-f])[0-9a-f]{12,}\b/gi;

/** A parenthesis left empty by a removed token. */
const EMPTY_PARENTHESES = /\s*\(\s*\)/g;

/** `text` without instrument fingerprints or other hashes, its spacing tidied. */
export function ccImageText(text: string | null | undefined): string {
  return (text ?? '')
    .replace(INSTRUMENT_TEXT, '')
    .replace(HEX_TOKEN, '')
    .replace(EMPTY_PARENTHESES, '')
    .replace(/[ \t]{2,}/g, ' ')
    .replace(/\s+([.,;:])/g, '$1')
    .trim();
}

// -----------------------------------------------------------------------------------------------
// Blocks
// -----------------------------------------------------------------------------------------------

/** A status icon, drawn from Feather path data. */
export type CcImageIcon = 'check' | 'alert' | 'minus' | 'info' | 'x';

/** The colors a chip, a pill or an outcome takes: the score tiers, the accent, the periods, or neutral. */
export type CcImageTone = 'high' | 'mid' | 'low' | 'neutral' | 'accent' | 'baseline' | 'comparison';

export interface CcImageFact {
  readonly term: string;
  readonly value: string;
}

export interface CcImageChip {
  readonly text: string;
  readonly tone: CcImageTone;
  readonly icon?: CcImageIcon;
  /** A dashed border, as *Not computable* reads. */
  readonly dashed?: boolean;
}

/** One note of an endpoint card; a minimum-sample shortfall comes first, with a warning icon. */
export interface CcImageNote {
  readonly text: string;
  readonly shortfall: boolean;
}

export interface CcImageEndpointCard {
  readonly id: string;
  readonly name: string;
  /** `Margin ±3 index points`. */
  readonly margin: string;
  readonly status: CcEndpointStatus;
  readonly statusText: string;
  /** The grade word, or null where it says no more than *Not established*. */
  readonly grade: string | null;
  readonly estimate: string;
  /** `95 % interval −19.7 to +6.8 %`, or null. */
  readonly interval: string | null;
  readonly geometry: CcIntervalGeometry | null;
  readonly ends: CcIntervalEnds;
  readonly facts: readonly CcImageFact[];
  readonly notes: readonly CcImageNote[];
  /** The notes past the visible ones and the robustness checks, as *More about* holds them. */
  readonly more: readonly string[];
}

export interface CcImagePeriodCard {
  readonly period: 'baseline' | 'comparison';
  readonly title: string;
  /** `2026-10-01 to 2026-10-08`, or one day. */
  readonly dates: string;
  /** `First: battery run #11 · Last: battery run #12`; empty when no unit was found. */
  readonly range: string;
  readonly facts: readonly CcImageFact[];
}

export interface CcImageAttributionCard {
  readonly label: string;
  readonly grade: string;
  readonly gradeText: string;
  readonly endpoints: readonly string[];
  readonly evidence: string;
  readonly eventRefs: readonly string[];
}

export interface CcImageAttributionGroup {
  readonly side: string;
  readonly title: string;
  readonly cards: readonly CcImageAttributionCard[];
}

export interface CcImageRunGroupCard {
  readonly kind: string;
  readonly period: string;
  readonly periodText: string;
  readonly endpointIds: readonly string[];
  readonly title: string;
  readonly facts: readonly CcImageFact[];
  readonly suggestions: readonly string[];
  readonly reasons: readonly string[];
  /** *Set up from run #96 (Board Suite)*, the runs whose setup the card repeats; empty for none. */
  readonly repeat: string;
  /** The re-grade's or the control's closing hint; empty for none. */
  readonly hint: string;
}

/** One block of a section image, in drawing order. The four card kinds go two to a row where wide. */
export type CcResultsImageBlock =
  | { readonly kind: 'outcome'; readonly eyebrow: string; readonly title: string; readonly detail: string; readonly icon: CcImageIcon; readonly tone: CcImageTone }
  | { readonly kind: 'heading'; readonly text: string }
  | { readonly kind: 'paragraph'; readonly text: string; readonly tone: 'body' | 'muted' | 'strong' | 'high' }
  | { readonly kind: 'facts'; readonly facts: readonly CcImageFact[] }
  | { readonly kind: 'badgeRows'; readonly rows: readonly ImageFactRow[] }
  | { readonly kind: 'chips'; readonly label: string; readonly chips: readonly CcImageChip[] }
  | { readonly kind: 'figures'; readonly figures: readonly CcResultKeyFigure[] }
  | { readonly kind: 'notice'; readonly title: string; readonly items: readonly string[] }
  | { readonly kind: 'bullets'; readonly title: string; readonly items: readonly string[] }
  | { readonly kind: 'uncomputed'; readonly title: string; readonly rows: readonly CcImageFact[] }
  | { readonly kind: 'endpoint'; readonly card: CcImageEndpointCard }
  | { readonly kind: 'period'; readonly card: CcImagePeriodCard }
  | { readonly kind: 'attribution'; readonly group: CcImageAttributionGroup }
  | { readonly kind: 'runGroup'; readonly card: CcImageRunGroupCard };

/** The block kinds laid out as cards in a grid. */
export const CC_IMAGE_CARD_KINDS: ReadonlySet<CcResultsImageBlock['kind']> =
  new Set<CcResultsImageBlock['kind']>(['endpoint', 'period', 'attribution', 'runGroup']);

/** What a section's image reads besides the result. */
export interface CcResultsImageContext {
  /** The step-1 runs: the next runs' suites and the periods' units. */
  readonly rows: readonly CcRunRow[];
  /** The step-1 battery runs: a battery analysis's units. */
  readonly batteryRows: readonly CcBatteryRunRow[];
  /** The Details tab's events in the analyzed span, by day. */
  readonly eventDays: readonly CcEventDay[];
}

/** Whether an item of the section, by its key, goes into the image. */
export type CcResultsImageInclude = (key: string) => boolean;

/** One choosable item of a section: its stable key, its label and, where it has one, its current value. */
export interface CcResultsImageItem {
  readonly key: string;
  readonly label: string;
  readonly value: string;
}

const STATUS_ICON: Readonly<Record<CcEndpointStatus, CcImageIcon>> = {
  within: 'check',
  improved: 'check',
  changed: 'alert',
  inconclusive: 'minus',
  notComputable: 'minus'
};

const STATUS_TONE: Readonly<Record<CcEndpointStatus, CcImageTone>> = {
  within: 'high',
  improved: 'high',
  changed: 'mid',
  inconclusive: 'neutral',
  notComputable: 'neutral'
};

const SHORTFALL_PREFIX = /^below the minimum sample:/i;

/**
 * The items a section's image may include, in drawing order. Items that depend on the result (an
 * endpoint card, a key figure, a next-run card) are listed only where the result has them.
 */
export function ccResultsImageItems(
  section: CcResultsImageSection,
  result: CcAnalysisResult,
  context: CcResultsImageContext
): CcResultsImageItem[] {
  const item = (key: string, label: string, value = ''): CcResultsImageItem => ({ key, label, value });
  switch (section) {
    case 'summary': {
      const items = [
        item('verdict', 'Verdict and detail', ccOverallOutcome(result).title),
        item('model', 'Model and compared set'),
        item('chips', 'Endpoint chips'),
        item('scope', 'Scope and protocol')
      ];
      if (result.headlineReliabilityIncreases.length > 0) items.push(item('reliability', 'Reliability notes'));
      for (const figure of ccResultKeyFigures(result)) items.push(item(`figure-${figure.key}`, figure.label, figure.value));
      return items;
    }
    case 'verdicts': {
      const items = ccComputedEndpoints(result)
        .map(endpoint => item(`endpoint-${endpoint.id}`, `${endpoint.id} ${endpoint.name}`, ccEndpointStatusText(endpoint)));
      if (result.endpoints.some(endpoint => !endpoint.computed)) items.push(item('uncomputed', 'Not computable endpoints'));
      items.push(
        item('meaning', 'What it means'),
        item('mde', 'Smallest detectable change'),
        item('compared', 'Compared'),
        item('notes', 'Warnings and notes'),
        item('more', 'More about details')
      );
      return items;
    }
    case 'periods':
      return [item('baseline', 'Baseline card'), item('comparison', 'Comparison card'), item('units', 'Runs in the periods')];
    case 'attribution':
      return [item('changes', 'Decisive changes'), item('groups', 'Attribution by side'), item('unattributed', 'Unattributed note')];
    case 'nextRuns': {
      const items = ccNextRunSections(result, context.rows)
        .flatMap(entry => entry.cards.map(card => item(`group-${card.key}`, card.title, card.periodText)));
      items.push(item('suggestions', 'Suggestions'), item('reasons', 'Reasons'));
      return items;
    }
    case 'details': {
      const items = [item('limitations', 'Limitations'), item('dataQuality', 'Data quality')];
      if (ccShownRunSelection(result)) items.push(item('selection', 'Run selection'));
      items.push(item('events', 'Overseer events'), item('identity', 'Analysis identity'));
      return items;
    }
  }
}

/** The blocks of a section's image, those `include` refuses left out. */
export function ccResultsImageBlocks(
  section: CcResultsImageSection,
  result: CcAnalysisResult,
  context: CcResultsImageContext,
  include: CcResultsImageInclude = () => true
): CcResultsImageBlock[] {
  switch (section) {
    case 'summary':
      return summaryBlocks(result, include);
    case 'verdicts':
      return verdictBlocks(result, include);
    case 'periods':
      return periodBlocks(result, context, include);
    case 'attribution':
      return attributionBlocks(result, include);
    case 'nextRuns':
      return nextRunBlocks(result, context, include);
    case 'details':
      return detailBlocks(result, context, include);
  }
}

/** Whether a section's blocks hold anything beyond the leads every image carries. */
export function ccResultsImageHasContent(blocks: readonly CcResultsImageBlock[]): boolean {
  return blocks.some(block => block.kind !== 'paragraph' || block.tone !== 'muted');
}

/** The section's name, as its tab reads. */
export function ccResultsImageSectionLabel(section: CcResultsImageSection): string {
  return CC_RESULTS_IMAGE_SECTION_LABELS[section];
}

// --- The model ---

function providerTone(provider: string): ImageBadgeTone {
  const key = provider.trim().toLowerCase();
  return key === 'openai' || key === 'anthropic' || key === 'google' ? key : 'provider';
}

/**
 * The model as the banner shows it — its name, thinking level, provider and service tier, then its id —
 * and, with `compared`, the compared set's kind and label.
 */
export function ccImageModelRows(result: CcAnalysisResult, compared: boolean): ImageFactRow[] {
  const subject = result.subject;
  const rows: ImageFactRow[] = [{
    label: 'Model',
    primary: true,
    runs: [
      { kind: 'text', text: ccModelBaseName(subject.displayName, subject.thinkingLevel), strong: true },
      { kind: 'badge', text: formatThinkingLevel(subject.thinkingLevel), tone: 'thinking' },
      { kind: 'badge', text: subject.provider || 'Unknown', tone: providerTone(subject.provider ?? '') },
      ...(subject.serviceTier ? [{ kind: 'badge' as const, text: formatServiceTier(subject.serviceTier), tone: 'config' as const }] : []),
      ...(subject.modelId ? [{ kind: 'text' as const, text: subject.modelId, muted: true }] : [])
    ]
  }];
  const set = result.comparisonSet;
  if (compared && set) {
    rows.push({
      label: 'Compared',
      primary: true,
      runs: [
        { kind: 'badge', text: set.kind === 'battery' ? 'Battery' : 'Suite', tone: 'role' },
        { kind: 'text', text: ccImageText(set.label) }
      ]
    });
  }
  return rows;
}

// --- Summary ---

const OUTCOME_LOOK: Readonly<Record<string, { icon: CcImageIcon; tone: CcImageTone }>> = {
  noChange: { icon: 'check', tone: 'high' },
  changed: { icon: 'alert', tone: 'mid' },
  undecided: { icon: 'minus', tone: 'accent' },
  noneComputed: { icon: 'minus', tone: 'neutral' }
};

function scopeValue(result: CcAnalysisResult): string {
  return result.scope.strataUsed.length === 0
    ? 'No common time stratum'
    : result.scope.text || result.scope.strataUsed.join(', ');
}

function summaryBlocks(result: CcAnalysisResult, include: CcResultsImageInclude): CcResultsImageBlock[] {
  const blocks: CcResultsImageBlock[] = [];
  if (include('verdict')) {
    const outcome = ccOverallOutcome(result);
    const look = OUTCOME_LOOK[outcome.kind] ?? OUTCOME_LOOK['noneComputed'];
    blocks.push({ kind: 'outcome', eyebrow: 'Verdict on the Overseer chat', title: outcome.title, detail: ccImageText(outcome.detail), ...look });
  }
  if (include('model')) {
    blocks.push({ kind: 'badgeRows', rows: ccImageModelRows(result, true) });
  }
  if (include('chips') && result.endpoints.length > 0) {
    blocks.push({
      kind: 'chips',
      label: 'Endpoints',
      chips: result.endpoints.map((endpoint): CcImageChip => {
        const status = ccEndpointStatus(endpoint);
        return {
          text: `${endpoint.id} ${endpoint.name}: ${ccEndpointStatusText(endpoint)}`,
          tone: STATUS_TONE[status],
          icon: STATUS_ICON[status],
          dashed: status === 'notComputable'
        };
      })
    });
  }
  if (include('scope')) {
    blocks.push({
      kind: 'facts',
      facts: [
        { term: 'Scope', value: ccImageText(scopeValue(result)) },
        { term: 'Protocol', value: ccImageText(result.protocolLabel) }
      ]
    });
  }
  if (include('reliability') && result.headlineReliabilityIncreases.length > 0) {
    blocks.push({ kind: 'notice', title: 'Reliability', items: result.headlineReliabilityIncreases.map(ccImageText) });
  }
  const figures = ccResultKeyFigures(result).filter(figure => include(`figure-${figure.key}`));
  if (figures.length > 0) {
    blocks.push({ kind: 'figures', figures });
  }
  return blocks;
}

// --- Verdicts ---

function comparedText(endpoint: CcEndpointResult, batteryAnalysis: boolean): string {
  const noun = batteryAnalysis ? 'member run' : 'run';
  const runs = `${plural(endpoint.baselineRunCount, noun)} vs ${plural(endpoint.comparisonRunCount, noun)}`;
  return endpoint.itemCount > 0 ? `${runs} · ${plural(endpoint.itemCount, 'paired item')}` : runs;
}

/** One computed endpoint as its card shows it, the parts `include` refuses left out. */
export function ccImageEndpointCard(
  endpoint: CcEndpointResult,
  batteryAnalysis: boolean,
  include: CcResultsImageInclude = () => true
): CcImageEndpointCard {
  const status = ccEndpointStatus(endpoint);
  const parts = ccEstimateParts(endpoint);
  const { meaning, notes } = ccEndpointNotes(endpoint);
  const facts: CcImageFact[] = [];
  if (meaning && include('meaning')) facts.push({ term: 'What it means', value: ccImageText(meaning) });
  if (status === 'inconclusive' && include('mde')) {
    const mde = endpointMdeText(endpoint);
    if (mde !== NO_VALUE) facts.push({ term: 'Smallest change this sample can detect', value: mde });
  }
  if (include('compared')) facts.push({ term: 'Compared', value: comparedText(endpoint, batteryAnalysis) });

  const ordered: CcImageNote[] = [
    ...notes.filter(note => SHORTFALL_PREFIX.test(note)),
    ...notes.filter(note => !SHORTFALL_PREFIX.test(note))
  ].map(text => ({ text: ccImageText(text), shortfall: SHORTFALL_PREFIX.test(text) }));
  const more = [
    ...ordered.slice(CC_EP_VISIBLE_NOTES).map(note => note.text),
    ...endpoint.robustnessChecks.map(check => {
      const word = CC_CHECK_STATUS_TEXT[check.status] ?? check.status;
      const detail = ccImageText(check.detail);
      return `${ccImageText(check.name)}: ${word}${detail ? ` — ${detail}` : ''}`;
    })
  ];
  return {
    id: endpoint.id,
    name: endpoint.name,
    margin: `Margin ${ccImageText(endpoint.marginText)}`,
    status,
    statusText: ccEndpointStatusText(endpoint),
    grade: endpoint.grade !== 'notEstablished' ? gradeText(endpoint.grade) : null,
    estimate: parts.estimate,
    interval: parts.interval ? `95 % interval ${parts.interval}` : null,
    geometry: ccIntervalGeometry(endpoint),
    ends: intervalEnds(endpoint.direction),
    facts,
    notes: include('notes') ? ordered.slice(0, CC_EP_VISIBLE_NOTES) : [],
    more: include('more') ? more : []
  };
}

function verdictBlocks(result: CcAnalysisResult, include: CcResultsImageInclude): CcResultsImageBlock[] {
  const blocks: CcResultsImageBlock[] = [{
    kind: 'paragraph',
    tone: 'muted',
    text: 'Each endpoint compares the comparison period with the baseline against a margin fixed in advance.'
  }];
  const battery = result.unitKind === 'batteryRun';
  for (const endpoint of ccComputedEndpoints(result)) {
    if (include(`endpoint-${endpoint.id}`)) {
      blocks.push({ kind: 'endpoint', card: ccImageEndpointCard(endpoint, battery, include) });
    }
  }
  const groups = ccNotComputableGroups(result.endpoints);
  if (groups.length > 0 && include('uncomputed')) {
    const count = result.endpoints.filter(endpoint => !endpoint.computed).length;
    blocks.push({
      kind: 'uncomputed',
      title: `Not computable (${count})`,
      rows: groups.map(group => ({
        term: group.endpoints.map(endpoint => `${endpoint.id} ${endpoint.name}`).join(' · '),
        value: ccImageText(group.reason)
      }))
    });
  }
  return blocks;
}

// --- Periods ---

function periodFacts(result: CcAnalysisResult, period: 'baseline' | 'comparison', summary: CcPeriodSummary): CcImageFact[] {
  const facts: CcImageFact[] = [];
  if (result.unitKind === 'batteryRun' && result.units && result.units.length > 0) {
    const count = result.units.filter(unit => unit.kind === 'batteryRun' && unit.period === period).length;
    facts.push({ term: 'Battery runs', value: formatInteger(count) });
  }
  facts.push(
    { term: 'Runs', value: formatInteger(summary.runCount) },
    { term: 'Days', value: formatInteger(summary.days.length) },
    { term: 'Answers', value: formatInteger(summary.answerCount) },
    { term: 'Items', value: formatInteger(summary.itemCount) },
    { term: 'Suites', value: summary.suiteNames.length > 0 ? summary.suiteNames.map(ccImageText).join(', ') : NO_VALUE }
  );
  if (summary.legacyRunCount > 0) {
    facts.push({ term: 'Runs without telemetry', value: formatInteger(summary.legacyRunCount) });
  }
  return facts;
}

function rangeText(noun: string, firstId: number | null, lastId: number | null): string {
  if (firstId !== null && firstId === lastId) return `First and last: ${noun} #${firstId}`;
  return [
    firstId !== null ? `First: ${noun} #${firstId}` : '',
    lastId !== null ? `Last: ${noun} #${lastId}` : ''
  ].filter(part => part !== '').join(' · ');
}

function periodBlocks(result: CcAnalysisResult, context: CcResultsImageContext, include: CcResultsImageInclude): CcResultsImageBlock[] {
  const blocks: CcResultsImageBlock[] = [{
    kind: 'paragraph',
    tone: 'muted',
    text: 'The baseline and comparison periods this analysis was computed over.'
  }];
  const battery = result.unitKind === 'batteryRun';
  const noun = battery ? 'battery run' : 'run';
  const view = ccResultPeriodUnits(result, context.rows, context.batteryRows);
  const cards: { period: 'baseline' | 'comparison'; title: string; summary: CcPeriodSummary; first: number | null; last: number | null }[] = [
    { period: 'baseline', title: 'Baseline', summary: result.baseline, first: view.ids.baselineFirstId, last: view.ids.baselineLastId },
    { period: 'comparison', title: 'Comparison', summary: result.comparison, first: view.ids.comparisonFirstId, last: view.ids.comparisonLastId }
  ];
  for (const card of cards) {
    if (!include(card.period)) continue;
    const start = formatUtcDate(card.summary.startUtc);
    const end = formatUtcDate(card.summary.endUtc);
    blocks.push({
      kind: 'period',
      card: {
        period: card.period,
        title: card.title,
        dates: end === start ? start : `${start} to ${end}`,
        range: rangeText(noun, card.first, card.last),
        facts: periodFacts(result, card.period, card.summary)
      }
    });
  }
  if (include('units') && (view.units.length > 0 || view.missing > 0)) {
    blocks.push({ kind: 'heading', text: battery ? 'Battery runs in the periods' : 'Runs in the periods' });
    for (const card of cards) {
      const items = view.units
        .filter(unit => view.assignment.get(unit.id) === card.period)
        .map(unit => {
          const runs = unit.battery
            ? ` · ${unit.runs.map(run => `#${run.runId} ${ccImageText(run.suiteName)}`).join(', ')}`
            : unit.runs[0] ? ` · ${ccImageText(unit.runs[0].suiteName)}` : '';
          return `${battery ? 'Battery run' : 'Run'} #${unit.id} · ${formatUtcDateTime(unit.startedAtUtc)}${runs}`;
        });
      if (items.length > 0) blocks.push({ kind: 'bullets', title: card.title, items });
    }
    if (view.missing > 0) {
      const verb = view.missing === 1 ? 'is' : 'are';
      blocks.push({ kind: 'paragraph', tone: 'body', text: `${plural(view.missing, noun)} of this analysis ${verb} not among the ${noun}s step 1 loaded.` });
    }
  }
  return blocks;
}

// --- Attribution ---

function attributionBlocks(result: CcAnalysisResult, include: CcResultsImageInclude): CcResultsImageBlock[] {
  const view = ccAttributionView(result);
  const blocks: CcResultsImageBlock[] = [];
  if (view.empty) {
    if (include('groups')) {
      blocks.push({ kind: 'paragraph', tone: 'strong', text: 'Nothing to attribute: no endpoint shows a decisive change.' });
      for (const attribution of result.attribution.attributions.filter(entry => entry.side === 'undetermined')) {
        blocks.push({ kind: 'paragraph', tone: 'muted', text: ccImageText(attribution.evidence) });
      }
    }
    return blocks;
  }
  if (include('changes') && view.decisiveChanges.length > 0) {
    blocks.push({
      kind: 'chips',
      label: 'Changes to attribute',
      chips: view.decisiveChanges.map((change): CcImageChip => ({
        text: `${ccImageText(change.name)} ${change.verdictText}`,
        tone: change.verdict === 'changedDegraded' ? 'low' : 'high'
      }))
    });
  }
  if (include('groups')) {
    for (const group of view.groups) {
      blocks.push({
        kind: 'attribution',
        group: {
          side: group.side,
          title: group.title,
          cards: group.attributions.map(attribution => ({
            label: ccImageText(attribution.label),
            grade: attribution.grade,
            gradeText: gradeText(attribution.grade),
            endpoints: attribution.endpoints,
            evidence: ccImageText(attribution.evidence),
            eventRefs: attribution.eventRefs.map(ccImageText).filter(ref => ref !== '')
          }))
        }
      });
    }
  }
  if (include('unattributed') && view.unattributedText) {
    blocks.push({ kind: 'paragraph', tone: 'muted', text: view.unattributedText });
  }
  return blocks;
}

// --- Next runs ---

function nextRunBlocks(result: CcAnalysisResult, context: CcResultsImageContext, include: CcResultsImageInclude): CcResultsImageBlock[] {
  const sections = ccNextRunSections(result, context.rows);
  if (sections.length === 0) {
    return [{ kind: 'paragraph', tone: 'high', text: 'No run is needed: no verdict is waiting on more data.' }];
  }
  const blocks: CcResultsImageBlock[] = [{
    kind: 'paragraph',
    tone: 'body',
    text: ccNextRunsLeadText(ccNextRunActionCount(ccNextRunGroups(result, context.rows)))
  }];
  const provider = result.subject.provider;
  for (const section of sections) {
    const cards = section.cards.filter(card => include(`group-${card.key}`));
    if (cards.length === 0) continue;
    blocks.push({ kind: 'heading', text: section.heading });
    if (section.lead) blocks.push({ kind: 'paragraph', tone: 'muted', text: section.lead });
    for (const card of cards) {
      const control = card.kind === 'control';
      const repeat = card.targets.length > 0
        ? `Set up from ${card.targets.map(target => {
          const suite = context.rows.find(row => row.runId === target.runId)?.suiteName;
          return `run #${target.runId}${suite ? ` (${ccImageText(suite)})` : ''}`;
        }).join(', ')}`
        : '';
      blocks.push({
        kind: 'runGroup',
        card: {
          kind: card.kind,
          period: card.period,
          periodText: card.periodText,
          endpointIds: card.endpointIds,
          title: card.title,
          facts: control
            ? [
              { term: 'Suite', value: ccImageText(card.suitesText) },
              { term: 'Same build as', value: card.buildText },
              { term: 'Provider', value: `not ${provider}` }
            ]
            : [],
          suggestions: include('suggestions') ? card.suggestions.map(ccImageText).filter(text => text !== '') : [],
          reasons: include('reasons') ? card.reasons.map(ccImageText).filter(text => text !== '') : [],
          repeat,
          hint: card.kind === 'regrade'
            ? 'Re-grade from step 3: Controls → Re-grade.'
            : control ? `Then choose a model from a provider other than ${provider}.` : ''
        }
      });
    }
  }
  return blocks;
}

// --- Details ---

function eventLines(days: readonly CcEventDay[]): string[] {
  const lines: string[] = [];
  for (const day of days) {
    for (const item of day.items) {
      if (item.kind === 'event') {
        const changes = eventGroupChangesText(item.group);
        lines.push(ccImageText(`${item.group.tag} · ${day.day} · ${item.group.title}${changes ? `: ${changes}` : ''}`));
      } else if (item.kind === 'annotation') {
        lines.push(ccImageText(`${item.tag} · ${day.day} · ${annotationKindText(item.annotation.kind)}: ${item.annotation.text}`));
      } else {
        const change = item.change;
        lines.push(ccImageText(`${change.tag} · ${day.day} · Served model changed from ${change.from} to ${change.to} (${change.unit} #${change.runId})`));
      }
    }
  }
  return lines;
}

function detailBlocks(result: CcAnalysisResult, context: CcResultsImageContext, include: CcResultsImageInclude): CcResultsImageBlock[] {
  const blocks: CcResultsImageBlock[] = [{
    kind: 'paragraph',
    tone: 'muted',
    text: 'Background for reading the verdicts. Nothing here changes them.'
  }];
  const selection = ccShownRunSelection(result);
  if (selection && include('selection')) {
    blocks.push({ kind: 'heading', text: 'Run selection' });
    if (selection.recorded) {
      const marks = ccRunSelectionMarks(result, selection);
      const bounds = ccRangeBoundsText(selection);
      blocks.push({
        kind: 'facts',
        facts: [
          { term: 'Dates', value: `${selection.rangeLabel || 'All dates'}${bounds ? ` · ${bounds}` : ''}` },
          { term: 'First run', value: marks.first },
          { term: 'Last run', value: marks.last },
          { term: 'Left out in step 1', value: marks.leftOut }
        ]
      });
    }
    const groups = ccUnanalyzedGroups(selection);
    if (groups.length > 0) {
      blocks.push({ kind: 'bullets', title: 'Not analyzed', items: groups.map(group => `${group.label}: ${group.runs}`) });
    } else {
      blocks.push({ kind: 'paragraph', tone: 'muted', text: 'Every usable run of the model in the periods was analyzed.' });
    }
  }
  if (include('events')) {
    const lines = eventLines(context.eventDays);
    blocks.push({ kind: 'heading', text: `Events in the analyzed span (${lines.length})` });
    blocks.push(lines.length > 0
      ? { kind: 'bullets', title: '', items: lines }
      : { kind: 'paragraph', tone: 'muted', text: 'No Overseer change, annotation or served-model change in this range.' });
  }
  if (include('limitations')) {
    blocks.push({ kind: 'heading', text: `Limitations (${result.limitations.length})` });
    blocks.push(result.limitations.length > 0
      ? { kind: 'bullets', title: '', items: result.limitations.map(ccImageText) }
      : { kind: 'paragraph', tone: 'muted', text: 'None recorded.' });
  }
  if (include('dataQuality')) {
    blocks.push({ kind: 'heading', text: `Data quality (${result.dataQuality.length})` });
    blocks.push(result.dataQuality.length > 0
      ? { kind: 'bullets', title: '', items: result.dataQuality.map(note => ccImageText(note.text)) }
      : { kind: 'paragraph', tone: 'muted', text: 'No data-quality notes.' });
  }
  if (include('identity')) {
    blocks.push({ kind: 'heading', text: 'About this analysis' });
    blocks.push({
      kind: 'facts',
      facts: [
        { term: 'Analysis', value: `#${result.analysisId ?? NO_VALUE}${result.name ? ` · ${ccImageText(result.name)}` : ''}` },
        { term: 'Saved', value: formatUtcDateTime(result.createdAtUtc) },
        { term: 'Protocol', value: ccImageText(result.protocolLabel) },
        { term: 'Analysis code', value: `version ${result.analysisCodeVersion}` }
      ]
    });
  }
  return blocks;
}

// -----------------------------------------------------------------------------------------------
// The image's own lines
// -----------------------------------------------------------------------------------------------

/** `Chat Consistency · Verdicts`. */
export function ccResultsImageTitle(section: CcResultsImageSection): string {
  return `Chat Consistency · ${ccResultsImageSectionLabel(section)}`;
}

/** `Analysis #4 · saved 2026-10-10 09:40 UTC · Protocol V1`; an unsaved analysis says so. */
export function ccResultsImageAnalysisLine(result: CcAnalysisResult): string {
  const protocol = ccImageText(result.protocolLabel);
  const parts = [
    result.analysisId !== null ? `Analysis #${result.analysisId}` : 'Unsaved analysis',
    result.createdAtUtc ? `saved ${formatUtcDateTime(result.createdAtUtc)}` : '',
    protocol ? `Protocol ${protocol}` : ''
  ];
  return parts.filter(part => part !== '').join(' · ');
}
