/**
 * The Chat Consistency events as the timeline presents them: Overseer events grouped into composite
 * events (one per UTC day and harness version), the served-model changes, the tagged annotations,
 * and the day-by-day event list. Pure and DOM-free; the charts and the event list read the same
 * groups and tags from here, so a tag means the same thing in both. The report charts group an
 * analysis's events as its report documents do instead (`ccReportEventGroups`).
 */

import { CcAnnotation, CcEvent, CcTimelinePoint } from './chat-consistency.models';
import { formatUtcDate, plural, utcMillis } from './chat-consistency-format';

/** What a chart marker stands for. */
export type CcMarkerKind = 'event' | 'annotation' | 'served';

/** Short names of OverseerEventKinds (server: ChatConsistencyComparability.cs), in the server's kind order. */
export const CC_EVENT_KIND_LABELS: Readonly<Record<string, string>> = Object.freeze({
  CandidateSystemPromptSha256: 'System prompt',
  ToolGuidesSha256: 'Tool guides',
  KnowledgeBaseHeadSha: 'Knowledge base',
  WikiHeadSha: 'Wiki',
  SourceCodeHeadSha: 'Source code',
  CorpusIndexFingerprintsJson: 'Corpus index',
  CandidatePromptOptionsJson: 'Prompt options',
  ToolIterationCapsJson: 'Tool iteration caps',
  TotalModelCallCapsJson: 'Model call caps',
  QuestionTimeoutSecondsJson: 'Question timeouts',
  MaxToolCallsPerQuestionUsed: 'Tool call budget',
  HarnessVersion: 'Harness'
});

/** The kind of a harness version change, whose `from` and `to` are harness identities. */
export const CC_HARNESS_EVENT_KIND = 'HarnessVersion';

const KIND_ORDER: readonly string[] = Object.keys(CC_EVENT_KIND_LABELS);

/** The short name of an event kind; an unknown kind is its own name. */
export function eventKindLabel(kind: string): string {
  return Object.hasOwn(CC_EVENT_KIND_LABELS, kind) ? CC_EVENT_KIND_LABELS[kind] : kind;
}

/** Known kinds in `CC_EVENT_KIND_LABELS` order, then unknown kinds in ordinal order. */
export function compareEventKinds(a: string, b: string): number {
  const rank = (kind: string) => {
    const index = KIND_ORDER.indexOf(kind);
    return index >= 0 ? index : KIND_ORDER.length;
  };
  return rank(a) - rank(b) || (a < b ? -1 : a > b ? 1 : 0);
}

/** The main version of a harness identity: `27` of `27 (re-run 28)`; null when empty. */
export function mainHarnessVersion(identity: string | null | undefined): string | null {
  if (!identity) return null;
  const at = identity.indexOf(' (re-run');
  const main = (at >= 0 ? identity.slice(0, at) : identity).trim();
  return main === '' ? null : main;
}

// --- Composite events ---

/** One kind of change inside a composite event. */
export interface CcEventChange {
  kind: string;
  label: string;
  /** The runs that detected this kind. */
  count: number;
}

/** The Overseer events of one UTC day under one harness version. */
export interface CcEventGroup {
  /** `${day}|${harnessVersion ?? ''}`; a report group's is `${day}|${series}|${harness}|${side}` (`ccReportEventGroups`). */
  key: string;
  /** E1, E2 … in time order of `atUtc`. */
  tag: string;
  /** yyyy-MM-dd, UTC. */
  day: string;
  /** The earliest event of the group. */
  atUtc: string;
  lastAtUtc: string;
  harnessVersion: string | null;
  /** From the group's HarnessVersion event, if any. */
  harnessChange: { from: string | null; to: string | null } | null;
  /** Distinct, ascending. */
  runIds: number[];
  title: string;
  /** One per kind, in `CC_EVENT_KIND_LABELS` order. */
  changes: CcEventChange[];
  /** Time order, then run id, then kind order. */
  events: CcEvent[];
}

function cleanHarness(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed === '' ? null : trimmed;
}

/**
 * The harness an event happened under: its run's point's, or for a harness change the main
 * version it changed to; null when neither is known.
 */
function harnessOfEvent(event: CcEvent, harnessByRun: ReadonlyMap<number, string | null>): string | null {
  const harness = harnessByRun.get(event.runId) ?? null;
  if (harness !== null) return harness;
  return event.kind === CC_HARNESS_EVENT_KIND ? mainHarnessVersion(event.to) : null;
}

function groupTitle(harnessVersion: string | null, harnessChange: CcEventGroup['harnessChange']): string {
  if (harnessChange) return `Harness ${harnessChange.from ?? 'unknown'} → ${harnessChange.to ?? 'unknown'}`;
  return harnessVersion !== null ? `Changes under harness ${harnessVersion}` : 'Overseer changes';
}

/**
 * The events grouped by UTC day and harness version, oldest first and tagged `E1`… in that order.
 * An event of unknown harness joins the earliest group of its day, or starts one of unknown harness;
 * a later known-harness event of that day with no group of its own joins that group, which takes
 * its harness. Events without a parsable time are left out.
 *
 * With a non-empty `numbering`, the tags are taken from those reference groups instead: see
 * {@link retagFromReference}. `points` serves only to look up each event's harness version.
 */
export function groupOverseerEvents(
  events: readonly CcEvent[],
  points: readonly CcTimelinePoint[],
  numbering?: readonly CcEventGroup[]
): CcEventGroup[] {
  const harnessByRun = new Map<number, string | null>();
  for (const point of points) harnessByRun.set(point.runId, cleanHarness(point.harnessVersion));

  const timed = events
    .map((event, index) => ({ event, index, at: utcMillis(event.atUtc) }))
    .filter(entry => Number.isFinite(entry.at))
    .sort((a, b) => a.at - b.at || a.event.runId - b.event.runId
      || compareEventKinds(a.event.kind, b.event.kind) || a.index - b.index);

  const drafts: { day: string; harness: string | null; events: CcEvent[] }[] = [];
  for (const { event, at } of timed) {
    const day = formatUtcDate(at);
    const harness = harnessOfEvent(event, harnessByRun);
    const ofDay = drafts.filter(draft => draft.day === day);
    let draft = harness === null ? ofDay[0] : ofDay.find(d => d.harness === harness);
    if (!draft && harness !== null) {
      draft = ofDay.find(d => d.harness === null);
      if (draft) draft.harness = harness;
    }
    if (!draft) {
      draft = { day, harness, events: [] };
      drafts.push(draft);
    }
    draft.events.push(event);
  }

  const groups: CcEventGroup[] = drafts.map((draft, i) => {
    const harnessEvent = draft.events.find(event => event.kind === CC_HARNESS_EVENT_KIND) ?? null;
    const harnessChange = harnessEvent ? { from: harnessEvent.from, to: harnessEvent.to } : null;
    const kinds = [...new Set(draft.events.map(event => event.kind))].sort(compareEventKinds);
    return {
      key: `${draft.day}|${draft.harness ?? ''}`,
      tag: `E${i + 1}`,
      day: draft.day,
      atUtc: draft.events[0].atUtc,
      lastAtUtc: draft.events[draft.events.length - 1].atUtc,
      harnessVersion: draft.harness,
      harnessChange,
      runIds: [...new Set(draft.events.map(event => event.runId))].sort((a, b) => a - b),
      title: groupTitle(draft.harness, harnessChange),
      changes: kinds.map(kind => ({
        kind,
        label: eventKindLabel(kind),
        count: new Set(draft.events.filter(event => event.kind === kind).map(event => event.runId)).size
      })),
      events: draft.events
    };
  });
  return numbering && numbering.length > 0 ? retagFromReference(groups, numbering) : groups;
}

/** A UTC instant as epoch ms: a number as it is, a string parsed; NaN for none. */
function instantMs(value: string | number | null | undefined): number {
  if (typeof value === 'number') return value;
  return utcMillis(value);
}

/**
 * An analysis's own events grouped and tagged as its report documents number them, so a report chart's
 * markers match the server's events table (`cc-event-groups.fixture.json` pins the two together). An
 * event's group key is its UTC day, its series (empty for the target series, else its subject key),
 * the harness it happened under and its side of the period split:
 *
 * - the harness is the `to` of the latest harness change of its series at or before it (an equal
 *   instant counts when that change's run id is not after its own), else the `from` of the series'
 *   earliest harness change, else empty;
 * - the side is `comparison` from the comparison's start, else `baseline` up to the baseline's end,
 *   else `between`.
 *
 * Each group's events are in time order, then run id; the groups are ordered by their first event's
 * time, its run id, the target series first, then the subject key, and tagged `E1`… in that order.
 * Events without a parsable time are left out.
 */
export function ccReportEventGroups(
  events: readonly CcEvent[],
  baselineEndUtc: string | number | null | undefined,
  comparisonStartUtc: string | number | null | undefined
): CcEventGroup[] {
  const baselineEnd = instantMs(baselineEndUtc);
  const comparisonStart = instantMs(comparisonStartUtc);
  const timed = events
    .map((event, index) => ({ event, index, at: utcMillis(event.atUtc) }))
    .filter(entry => Number.isFinite(entry.at));
  const byTimeThenRun = (a: { at: number; event: CcEvent; index: number }, b: { at: number; event: CcEvent; index: number }) =>
    a.at - b.at || a.event.runId - b.event.runId || a.index - b.index;
  const seriesOf = (event: CcEvent) => event.inTargetSeries ? '' : (event.subjectKey ?? '');

  // The harness changes of each series, in time order, then run id.
  const harnessChanges = new Map<string, typeof timed>();
  for (const entry of timed.filter(e => e.event.kind === CC_HARNESS_EVENT_KIND).sort(byTimeThenRun)) {
    const series = `${entry.event.inTargetSeries ? 'target' : 'other'}|${seriesOf(entry.event)}`;
    harnessChanges.set(series, [...(harnessChanges.get(series) ?? []), entry]);
  }
  const harnessOf = (event: CcEvent, at: number): string => {
    const changes = harnessChanges.get(`${event.inTargetSeries ? 'target' : 'other'}|${seriesOf(event)}`) ?? [];
    let latest: (typeof timed)[number] | null = null;
    for (const change of changes) {
      if (change.at < at || (change.at === at && change.event.runId <= event.runId)) latest = change;
    }
    if (latest) return latest.event.to ?? '';
    return changes.length > 0 ? changes[0].event.from ?? '' : '';
  };
  const sideOf = (at: number): string => {
    if (Number.isFinite(comparisonStart) && at >= comparisonStart) return 'comparison';
    if (Number.isFinite(baselineEnd) && at <= baselineEnd) return 'baseline';
    return 'between';
  };

  const drafts = new Map<string, { key: string; harness: string; series: string; target: boolean; entries: typeof timed }>();
  for (const entry of timed) {
    const harness = harnessOf(entry.event, entry.at);
    const series = seriesOf(entry.event);
    const key = `${formatUtcDate(entry.at)}|${series}|${harness}|${sideOf(entry.at)}`;
    let draft = drafts.get(key);
    if (!draft) {
      draft = { key, harness, series, target: entry.event.inTargetSeries, entries: [] };
      drafts.set(key, draft);
    }
    draft.entries.push(entry);
  }
  const ordinal = (a: string, b: string) => (a < b ? -1 : a > b ? 1 : 0);
  const ordered = [...drafts.values()]
    .map(draft => ({ ...draft, entries: [...draft.entries].sort(byTimeThenRun) }))
    .sort((a, b) => a.entries[0].at - b.entries[0].at
      || a.entries[0].event.runId - b.entries[0].event.runId
      || Number(b.target) - Number(a.target)
      || ordinal(a.series, b.series)
      || ordinal(a.key, b.key));

  return ordered.map((draft, i) => {
    const groupEvents = draft.entries.map(entry => entry.event);
    const harnessVersion = draft.harness === '' ? null : draft.harness;
    const harnessEvent = groupEvents.find(event => event.kind === CC_HARNESS_EVENT_KIND) ?? null;
    const harnessChange = harnessEvent ? { from: harnessEvent.from, to: harnessEvent.to } : null;
    const kinds = [...new Set(groupEvents.map(event => event.kind))].sort(compareEventKinds);
    return {
      key: draft.key,
      tag: `E${i + 1}`,
      day: formatUtcDate(draft.entries[0].at),
      atUtc: groupEvents[0].atUtc,
      lastAtUtc: groupEvents[groupEvents.length - 1].atUtc,
      harnessVersion,
      harnessChange,
      runIds: [...new Set(groupEvents.map(event => event.runId))].sort((a, b) => a - b),
      title: groupTitle(harnessVersion, harnessChange),
      changes: kinds.map(kind => ({
        kind,
        label: eventKindLabel(kind),
        count: new Set(groupEvents.filter(event => event.kind === kind).map(event => event.runId)).size
      })),
      events: groupEvents
    };
  });
}

function eventTagNumber(tag: string): number {
  const match = /^E(\d+)$/.exec(tag);
  return match ? Number(match[1]) : 0;
}

/**
 * The groups, in their order, tagged after the reference groups: a group takes the tag of the
 * reference group with its key; a group of unknown harness takes the tag of its day's earliest
 * reference group, as such an event joins its day's earliest group. A group left without a tag,
 * or whose tag an earlier match took, is numbered after the reference's highest number, in time
 * order, so no tag names two composites.
 */
function retagFromReference(groups: readonly CcEventGroup[], reference: readonly CcEventGroup[]): CcEventGroup[] {
  const tagByKey = new Map<string, string>();
  const earliestOfDay = new Map<string, { tag: string; at: number }>();
  let next = 0;
  for (const ref of reference) {
    if (!tagByKey.has(ref.key)) tagByKey.set(ref.key, ref.tag);
    const at = utcMillis(ref.atUtc);
    const known = earliestOfDay.get(ref.day);
    if (!known || at < known.at) earliestOfDay.set(ref.day, { tag: ref.tag, at });
    next = Math.max(next, eventTagNumber(ref.tag));
  }

  const tags: (string | null)[] = groups.map(() => null);
  const used = new Set<string>();
  const claim = (index: number, tag: string | undefined) => {
    if (tag === undefined || used.has(tag) || tags[index] !== null) return;
    tags[index] = tag;
    used.add(tag);
  };
  // Exact keys first, so a same-day unknown-harness group cannot take the tag of a keyed match.
  groups.forEach((group, i) => claim(i, tagByKey.get(group.key)));
  groups.forEach((group, i) => {
    if (group.harnessVersion === null) claim(i, earliestOfDay.get(group.day)?.tag);
  });
  return groups.map((group, i) => ({ ...group, tag: tags[i] ?? `E${++next}` }));
}

/**
 * One line for a composite event, the chart marker's label and the preset option: its title and day,
 * the kinds that changed (the harness itself left out when the title names it), and the run count
 * when more than one run showed the changes.
 */
export function eventGroupLabel(group: CcEventGroup): string {
  const labels = group.changes
    .filter(change => !(group.harnessChange && change.kind === CC_HARNESS_EVENT_KIND))
    .map(change => change.label);
  const runs = group.runIds.length > 1 ? ` (${plural(group.runIds.length, 'run')})` : '';
  return `${group.title} on ${group.day}${labels.length > 0 ? `: ${labels.join(', ')}` : ''}${runs}`;
}

/**
 * A composite event's kinds as one line, `System prompt, Knowledge base ×2`: a kind more than one run
 * detected carries the count, and the harness is left out when the title names it.
 */
export function eventGroupChangesText(group: CcEventGroup): string {
  return group.changes
    .filter(change => !(group.harnessChange && change.kind === CC_HARNESS_EVENT_KIND))
    .map(change => change.count > 1 ? `${change.label} ×${change.count}` : change.label)
    .join(', ');
}

/**
 * Every kind present in the groups, in kind order, each with the number of composite events that
 * contain it.
 */
export function eventKindSummary(groups: readonly CcEventGroup[]): CcEventChange[] {
  const counts = new Map<string, number>();
  for (const group of groups) {
    for (const change of group.changes) counts.set(change.kind, (counts.get(change.kind) ?? 0) + 1);
  }
  return [...counts.keys()].sort(compareEventKinds)
    .map(kind => ({ kind, label: eventKindLabel(kind), count: counts.get(kind) ?? 0 }));
}

// --- Served-model changes and annotations ---

/** A run whose dominant served model differs from the previous run's. */
export interface CcServedChange {
  /** S1, S2 … in time order. */
  tag: string;
  atUtc: string;
  runId: number;
  /** What `runId` names: `run`, or `battery run` for a battery point. */
  unit: string;
  from: string;
  to: string;
}

/** The provider-reported model id that served most of a run's calls, or null. */
export function dominantServedModel(point: CcTimelinePoint): string | null {
  let best: { modelId: string; callCount: number } | null = null;
  for (const entry of point.servedModelIds) {
    if (!best || entry.callCount > best.callCount) best = entry;
  }
  return best?.modelId ?? null;
}

/**
 * The runs, in time order, whose dominant served model differs from the latest earlier run that
 * had one; runs without a parsable start or a served model are skipped.
 */
export function servedModelChanges(points: readonly CcTimelinePoint[]): CcServedChange[] {
  const ordered = points
    .filter(point => Number.isFinite(utcMillis(point.startedAtUtc)))
    .slice()
    .sort((a, b) => utcMillis(a.startedAtUtc) - utcMillis(b.startedAtUtc) || a.runId - b.runId);
  const changes: CcServedChange[] = [];
  let previous: string | null = null;
  for (const point of ordered) {
    const current = dominantServedModel(point);
    if (current === null) continue;
    if (previous !== null && current !== previous) {
      const unit = 'memberRunIds' in point ? 'battery run' : 'run';
      changes.push({ tag: `S${changes.length + 1}`, atUtc: point.startedAtUtc, runId: point.runId, unit, from: previous, to: current });
    }
    previous = current;
  }
  return changes;
}

/** The marker label of a served-model change. */
export function servedChangeLabel(change: CcServedChange): string {
  return `Served model changed from ${change.from} to ${change.to} (${change.unit} #${change.runId})`;
}

export interface CcTaggedAnnotation {
  /** A1, A2 … in time order. */
  tag: string;
  annotation: CcAnnotation;
}

/** The annotations in time order (ties in input order), tagged `A1`…; those without a parsable time are left out. */
export function taggedAnnotations(annotations: readonly CcAnnotation[]): CcTaggedAnnotation[] {
  return annotations
    .map((annotation, index) => ({ annotation, index, at: utcMillis(annotation.atUtc) }))
    .filter(entry => Number.isFinite(entry.at))
    .sort((a, b) => a.at - b.at || a.index - b.index)
    .map((entry, i) => ({ tag: `A${i + 1}`, annotation: entry.annotation }));
}

// --- The event list ---

export type CcEventListItem =
  | { kind: 'event'; group: CcEventGroup }
  | { kind: 'annotation'; tag: string; annotation: CcAnnotation }
  | { kind: 'served'; change: CcServedChange };

export interface CcEventDay {
  /** yyyy-MM-dd, UTC. */
  day: string;
  items: CcEventListItem[];
}

/** Which markers show: the marker kinds, and the Overseer change kinds hidden. */
export interface CcMarkerFilter {
  kinds: ReadonlySet<CcMarkerKind>;
  hiddenEventKinds: ReadonlySet<string>;
}

/** A composite event shows while event markers show and any of its kinds is not hidden. */
export function eventGroupShown(group: CcEventGroup, filter?: CcMarkerFilter): boolean {
  if (!filter) return true;
  return filter.kinds.has('event') && group.changes.some(change => !filter.hiddenEventKinds.has(change.kind));
}

/**
 * The event list by UTC day, days and items oldest first; at equal times events come before
 * annotations, and annotations before served-model changes. The filter drops items and never
 * renumbers them: tags are those of the unfiltered groups, annotations and changes.
 */
export function buildEventDays(
  groups: readonly CcEventGroup[],
  annotations: readonly CcAnnotation[],
  served: readonly CcServedChange[],
  filter?: CcMarkerFilter
): CcEventDay[] {
  const shows = (kind: CcMarkerKind) => !filter || filter.kinds.has(kind);
  const entries: { item: CcEventListItem; at: number; rank: number; seq: number }[] = [];
  if (shows('event')) {
    groups.forEach((group, seq) => {
      if (eventGroupShown(group, filter)) entries.push({ item: { kind: 'event', group }, at: utcMillis(group.atUtc), rank: 0, seq });
    });
  }
  if (shows('annotation')) {
    taggedAnnotations(annotations).forEach(({ tag, annotation }, seq) =>
      entries.push({ item: { kind: 'annotation', tag, annotation }, at: utcMillis(annotation.atUtc), rank: 1, seq }));
  }
  if (shows('served')) {
    served.forEach((change, seq) =>
      entries.push({ item: { kind: 'served', change }, at: utcMillis(change.atUtc), rank: 2, seq }));
  }

  const days: CcEventDay[] = [];
  for (const entry of entries.filter(e => Number.isFinite(e.at)).sort((a, b) => a.at - b.at || a.rank - b.rank || a.seq - b.seq)) {
    const day = formatUtcDate(entry.at);
    let last = days[days.length - 1];
    if (!last || last.day !== day) {
      last = { day, items: [] };
      days.push(last);
    }
    last.items.push(entry.item);
  }
  return days;
}

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const;

/** The weekday of a `yyyy-MM-dd` UTC day, `Monday`; empty for an invalid day. */
export function utcWeekday(day: string): string {
  const at = Date.parse(`${day}T00:00:00Z`);
  return Number.isFinite(at) ? WEEKDAYS[new Date(at).getUTCDay()] : '';
}
