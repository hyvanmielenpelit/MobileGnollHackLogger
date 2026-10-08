import { ChangeDetectionStrategy, Component, Input, OnChanges } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import {
  CC_HARNESS_EVENT_KIND,
  CcEventDay,
  CcEventGroup,
  CcEventListItem,
  CcServedChange,
  eventKindLabel,
  utcWeekday
} from '../chat-consistency-events';
import { annotationKindText, formatInteger, parseUtc, plural } from '../chat-consistency-format';
import { CcAnnotation, CcEvent } from '../chat-consistency.models';

/** A detected value as the details list shows it; a long one is cut to its first characters. */
export interface CcEventValueView {
  text: string;
  /** The value was longer than `CC_EVENT_VALUE_MAX` and is cut. */
  cut: boolean;
}

/** One detection inside a composite event's *Details*. */
export interface CcEventDetailView {
  /** `Run #78`. */
  run: string;
  /** The series that showed the change, when it is not the target series. */
  subject: string | null;
  /** `Prompt options`. */
  label: string;
  /** Null when the earlier run had no value. */
  from: CcEventValueView | null;
  to: CcEventValueView | null;
}

/** One change chip: `System prompt ×3`. */
export interface CcEventChipView {
  kind: string;
  label: string;
  /** Runs that detected the kind; the chip shows it only above 1. */
  count: number;
}

interface CcEventRowBase {
  tag: string;
  /** Unique in the document: `${idPrefix}-item-${tag}`. */
  id: string;
  atUtc: string;
}

export interface CcEventRowEvent extends CcEventRowBase {
  kind: 'event';
  title: string;
  /** `Run #80 · 09:14 UTC`, `Runs #77–#79 · 09:14–16:52 UTC`. */
  meta: string;
  chips: CcEventChipView[];
  details: CcEventDetailView[];
}

export interface CcEventRowAnnotation extends CcEventRowBase {
  kind: 'annotation';
  title: string;
  text: string;
  /** `All providers`, `OpenAI`, `OpenAI · gpt-5`. */
  scope: string;
  time: string;
  source: string | null;
  sourceIsLink: boolean;
}

export interface CcEventRowServed extends CcEventRowBase {
  kind: 'served';
  from: string;
  to: string;
  /** `run #81`. */
  run: string;
  time: string;
}

export type CcEventRow = CcEventRowEvent | CcEventRowAnnotation | CcEventRowServed;

export interface CcEventDayView {
  day: string;
  /** The day heading's id: `${idPrefix}-day-${day}`. */
  headingId: string;
  weekday: string;
  /** `3 items`. */
  count: string;
  rows: CcEventRow[];
}

/** A detected value longer than this is shown cut. */
export const CC_EVENT_VALUE_MAX = 12;
/** The characters a cut value keeps. */
export const CC_EVENT_VALUE_CUT = 8;

function pad2(value: number): string {
  return value.toString().padStart(2, '0');
}

/** `09:14`, the UTC time of a server time; empty when it does not parse. */
export function utcClock(value: string): string {
  const date = parseUtc(value);
  return Number.isNaN(date.getTime()) ? '' : `${pad2(date.getUTCHours())}:${pad2(date.getUTCMinutes())}`;
}

/**
 * A detected value for the details list: whole up to `CC_EVENT_VALUE_MAX` characters, else its
 * first `CC_EVENT_VALUE_CUT`. A harness identity (`27 (re-run 28)`) is always whole.
 */
export function eventValueView(value: string | null, kind: string): CcEventValueView | null {
  if (value === null || value === '') return null;
  if (kind === CC_HARNESS_EVENT_KIND || value.length <= CC_EVENT_VALUE_MAX) return { text: value, cut: false };
  return { text: value.slice(0, CC_EVENT_VALUE_CUT), cut: true };
}

/** `Run #80`, `Runs #77–#79`, and for runs with gaps between them `Runs #77–#83 (4 runs)`. */
export function eventRunsText(runIds: readonly number[]): string {
  if (runIds.length === 0) return '';
  if (runIds.length === 1) return `Run #${runIds[0]}`;
  const first = runIds[0];
  const last = runIds[runIds.length - 1];
  const contiguous = last - first + 1 === runIds.length;
  return `Runs #${first}–#${last}${contiguous ? '' : ` (${plural(runIds.length, 'run')})`}`;
}

/** `09:14 UTC`, or `09:14–16:52 UTC` when the group spans more than one minute. */
export function eventTimeText(atUtc: string, lastAtUtc: string): string {
  const start = utcClock(atUtc);
  const end = utcClock(lastAtUtc);
  if (start === '') return '';
  return end === '' || end === start ? `${start} UTC` : `${start}–${end} UTC`;
}

/** Who an annotation applies to: `All providers`, a provider, or a provider's model. */
export function annotationScopeText(annotation: CcAnnotation): string {
  if (!annotation.provider) return 'All providers';
  return annotation.modelId ? `${annotation.provider} · ${annotation.modelId}` : annotation.provider;
}

/** A source is a link only for an http or https URL. */
export function isHttpUrl(url: string | null): boolean {
  return !!url && /^https?:\/\//i.test(url);
}

function eventRow(group: CcEventGroup, id: string): CcEventRowEvent {
  const time = eventTimeText(group.atUtc, group.lastAtUtc);
  return {
    kind: 'event',
    tag: group.tag,
    id,
    atUtc: group.atUtc,
    title: group.title,
    meta: [eventRunsText(group.runIds), time].filter(part => part !== '').join(' · '),
    chips: group.changes.map(change => ({ kind: change.kind, label: change.label, count: change.count })),
    details: group.events.map((event: CcEvent) => ({
      run: `Run #${event.runId}`,
      subject: event.inTargetSeries ? null : event.subjectKey,
      label: eventKindLabel(event.kind),
      from: eventValueView(event.from, event.kind),
      to: eventValueView(event.to, event.kind)
    }))
  };
}

function annotationRow(tag: string, annotation: CcAnnotation, id: string): CcEventRowAnnotation {
  const time = utcClock(annotation.atUtc);
  return {
    kind: 'annotation',
    tag,
    id,
    atUtc: annotation.atUtc,
    title: annotationKindText(annotation.kind),
    text: annotation.text,
    scope: annotationScopeText(annotation),
    time: time === '' ? '' : `${time} UTC`,
    source: annotation.sourceUrl?.trim() || null,
    sourceIsLink: isHttpUrl(annotation.sourceUrl?.trim() ?? null)
  };
}

function servedRow(change: CcServedChange, id: string): CcEventRowServed {
  const time = utcClock(change.atUtc);
  return {
    kind: 'served',
    tag: change.tag,
    id,
    atUtc: change.atUtc,
    from: change.from,
    to: change.to,
    run: `${change.unit} #${change.runId}`,
    time: time === '' ? '' : `${time} UTC`
  };
}

function itemTag(item: CcEventListItem): string {
  switch (item.kind) {
    case 'event': return item.group.tag;
    case 'annotation': return item.tag;
    case 'served': return item.change.tag;
  }
}

/** The rows of the event list, with ids under `idPrefix`. */
export function eventDayViews(days: readonly CcEventDay[], idPrefix: string): CcEventDayView[] {
  return days.map(day => ({
    day: day.day,
    headingId: `${idPrefix}-day-${day.day}`,
    weekday: utcWeekday(day.day),
    count: plural(day.items.length, 'item'),
    rows: day.items.map((item): CcEventRow => {
      const id = `${idPrefix}-item-${itemTag(item)}`;
      switch (item.kind) {
        case 'event': return eventRow(item.group, id);
        case 'annotation': return annotationRow(item.tag, item.annotation, id);
        case 'served': return servedRow(item.change, id);
      }
    })
  }));
}

/**
 * The summary line: `5 Overseer changes on 3 days · 1 annotation · 2 served-model changes`, then
 * `· 2 hidden by the filters` when the filters hide any; `Nothing shown · 2 hidden by the filters`
 * when they hide every item; empty when there is nothing at all.
 */
export function eventListSummary(days: readonly CcEventDay[], hiddenCount: number): string {
  let events = 0;
  let annotations = 0;
  let served = 0;
  let eventDays = 0;
  for (const day of days) {
    let dayHasEvent = false;
    for (const item of day.items) {
      if (item.kind === 'event') {
        events++;
        dayHasEvent = true;
      } else if (item.kind === 'annotation') {
        annotations++;
      } else {
        served++;
      }
    }
    if (dayHasEvent) eventDays++;
  }
  const parts: string[] = [];
  if (events > 0) parts.push(`${plural(events, 'Overseer change')} on ${plural(eventDays, 'day')}`);
  if (annotations > 0) parts.push(plural(annotations, 'annotation'));
  if (served > 0) parts.push(plural(served, 'served-model change'));
  if (hiddenCount > 0) {
    if (parts.length === 0) parts.push('Nothing shown');
    parts.push(`${formatInteger(hiddenCount)} hidden by the filters`);
  }
  return parts.join(' · ');
}

/**
 * The Overseer changes, annotations and served-model changes of a range, day by day: a summary
 * line, then one section per UTC day under a sticky heading, each item a tag pill of the shape the
 * charts draw and a body. Hosts may set `--cc-ev-surface` (the sticky heading's opaque ground) and
 * `--cc-ev-sticky-top` (its offset under a sticky bar of their own).
 */
@Component({
  selector: 'app-cc-event-list',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './cc-event-list.component.html',
  styleUrls: ['./cc-event-list.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcEventListComponent implements OnChanges {
  @Input({ required: true }) days: readonly CcEventDay[] = [];
  /** Prefixes every id in the list, so two lists can share a document. */
  @Input({ required: true }) idPrefix = '';
  @Input() dayHeadingLevel: 5 | 6 = 5;
  /** Items the filters hide, for the summary line. */
  @Input() hiddenCount = 0;

  views: CcEventDayView[] = [];
  summary = '';

  ngOnChanges(): void {
    this.views = eventDayViews(this.days, this.idPrefix);
    this.summary = eventListSummary(this.days, this.hiddenCount);
  }

  get summaryId(): string {
    return `${this.idPrefix}-summary`;
  }
}
