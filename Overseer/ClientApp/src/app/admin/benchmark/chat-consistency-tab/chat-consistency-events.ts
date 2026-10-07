/**
 * The Chat Consistency events as the timeline presents them: Overseer events grouped into composite
 * events (one per UTC day and harness version), the served-model changes, the tagged annotations,
 * and the day-by-day event list. Pure and DOM-free; the charts and the event list read the same
 * groups and tags from here, so a tag means the same thing in both.
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
  /** `${day}|${harnessVersion ?? ''}`. */
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
 */
export function groupOverseerEvents(events: readonly CcEvent[], points: readonly CcTimelinePoint[]): CcEventGroup[] {
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

  return drafts.map((draft, i) => {
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
      changes.push({ tag: `S${changes.length + 1}`, atUtc: point.startedAtUtc, runId: point.runId, from: previous, to: current });
    }
    previous = current;
  }
  return changes;
}

/** The marker label of a served-model change. */
export function servedChangeLabel(change: CcServedChange): string {
  return `Served model changed from ${change.from} to ${change.to} (run #${change.runId})`;
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
