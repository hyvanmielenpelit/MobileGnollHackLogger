import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input, OnInit, Output, inject } from '@angular/core';

import { ensureOverlayPolyfills } from '../../../../../utils/polyfills.util';
import { prefersReducedMotion } from '../../chat-consistency-charts';
import { CcEventGroup, CcTaggedAnnotation, eventGroupChangesText } from '../../chat-consistency-events';
import {
  NO_VALUE,
  annotationKindText,
  axisText,
  batteryRunStatusText,
  ccNumber,
  controlRunsText,
  formatFixed,
  formatInteger,
  formatMs,
  formatTokenRate,
  formatUsd,
  formatUtcDate,
  formatUtcDateTime,
  plural,
  regradeCoverageText,
  runStatusText,
  segmentText,
  servedModelsText,
  utcMillis
} from '../../chat-consistency-format';
import {
  CC_NO_PERIOD_IDS,
  CC_PERIOD_BOUND_LABELS,
  CcPeriod,
  CcPeriodBound,
  CcPeriodIds,
  CcPeriodUnit,
  CcUnitPeriod
} from '../../chat-consistency-periods';
import {
  CC_AXES,
  CcAxis,
  CcAxisEligibility,
  CcBatteryRunRow,
  CcBatteryTimelinePoint,
  CcRunRow,
  CcTimelinePoint
} from '../../chat-consistency.models';

/** A composite Overseer change or a tagged annotation, listed between the units it falls between. */
export interface CcPeriodMarker {
  kind: 'event' | 'annotation';
  /** E2, A1. */
  tag: string;
  /** yyyy-MM-dd, UTC. */
  day: string;
  /** The composite's title, or the annotation's text. */
  title: string;
  /** The kinds that changed, or the annotation's kind; empty for none. */
  kinds: string;
  /** What Split here emits: the group's key, or the annotation id. */
  splitKey: string;
  splitLabel: string;
}

/** A row of the list: a unit's card, or a marker between two units. */
export type CcPeriodUnitsItem =
  | { kind: 'unit'; key: string; unit: CcPeriodUnit }
  | { kind: 'marker'; key: string; marker: CcPeriodMarker };

/** One figure of a card, with an optional qualifier line. */
export interface CcPeriodMetric {
  key: 'intelligence' | 'firstAnswer' | 'streaming' | 'work' | 'cost' | 'answers';
  label: string;
  value: string;
  note: string | null;
}

export interface CcPeriodEligibility {
  axis: CcAxis;
  label: string;
  eligible: boolean;
}

/** Everything a card shows that does not depend on the bounds or the periods. */
export interface CcPeriodUnitCard {
  id: number;
  battery: boolean;
  /** The suite name of a run, the battery name of a battery run. */
  title: string;
  status: string;
  harness: string;
  isLegacy: boolean;
  isAnchor: boolean;
  /** `Battery run #12 · suite 1 of 2` on a run that is a battery member; empty otherwise. */
  memberTag: string;
  startedAtUtc: string;
  started: string;
  /** `Served: …` for a run, `2 of 2 suites · revision 1` for a battery run. */
  meta: string;
  boundsLabel: string;
  boundLabels: Readonly<Record<CcPeriodBound, string>>;
  reportLabel: string;
  reportTip: string;
  eligibilityLabel: string;
  eligibility: CcPeriodEligibility[];
  reasons: { axis: string; reason: string }[];
  metrics: CcPeriodMetric[];
  /** A run's facts; empty for a battery run. */
  segment: string;
  telemetry: string;
  regrade: string;
  /** The run's matched controls, or the battery run's members' together. */
  controls: string;
  strata: string;
  /** A battery run's usable members, in suite order; empty for a run. */
  members: { runId: number; text: string }[];
  membersLabel: string;
}

/** One row of a card's bounds grid: a period's label and its First and Last toggles. */
interface CcBoundRow {
  period: CcPeriod;
  label: string;
  bounds: readonly { key: CcPeriodBound; word: string }[];
}

const BOUND_ROWS: readonly CcBoundRow[] = [
  {
    period: 'baseline', label: 'Baseline',
    bounds: [{ key: 'baselineFirstId', word: 'First' }, { key: 'baselineLastId', word: 'Last' }]
  },
  {
    period: 'comparison', label: 'Comparison',
    bounds: [{ key: 'comparisonFirstId', word: 'First' }, { key: 'comparisonLastId', word: 'Last' }]
  }
];

const PERIOD_TEXT: Readonly<Record<CcUnitPeriod, string>> = {
  baseline: 'Baseline',
  comparison: 'Comparison',
  notUsed: 'Not used',
  notEligible: 'Not eligible'
};

/**
 * The units with the markers between them, oldest first. A marker goes before the first unit that
 * started at or after it; one before the first unit or after the last is not listed. Markers before
 * one unit are in time order, events before annotations at equal times.
 */
export function periodUnitItems(
  units: readonly CcPeriodUnit[],
  groups: readonly CcEventGroup[],
  annotations: readonly CcTaggedAnnotation[]
): CcPeriodUnitsItem[] {
  const markers: { at: number; rank: number; seq: number; item: CcPeriodUnitsItem }[] = [];
  groups.forEach((group, seq) => markers.push({
    at: utcMillis(group.atUtc),
    rank: 0,
    seq,
    item: {
      kind: 'marker',
      key: `e:${group.key}`,
      marker: {
        kind: 'event',
        tag: group.tag,
        day: group.day,
        title: group.title,
        kinds: eventGroupChangesText(group),
        splitKey: group.key,
        splitLabel: `Split the periods at Overseer change ${group.tag}`
      }
    }
  }));
  annotations.forEach(({ tag, annotation }, seq) => markers.push({
    at: utcMillis(annotation.atUtc),
    rank: 1,
    seq,
    item: {
      kind: 'marker',
      key: `a:${annotation.id}`,
      marker: {
        kind: 'annotation',
        tag,
        day: formatUtcDate(annotation.atUtc),
        title: annotation.text,
        kinds: annotationKindText(annotation.kind),
        splitKey: String(annotation.id),
        splitLabel: `Split the periods at annotation ${tag}`
      }
    }
  }));
  const ordered = markers
    .filter(marker => Number.isFinite(marker.at))
    .sort((a, b) => a.at - b.at || a.rank - b.rank || a.seq - b.seq);

  const items: CcPeriodUnitsItem[] = [];
  let next = 0;
  units.forEach((unit, index) => {
    for (; next < ordered.length && ordered[next].at <= unit.at; next++) {
      if (index > 0) items.push(ordered[next].item);
    }
    items.push({ kind: 'unit', key: `u:${unit.id}`, unit });
  });
  return items;
}

function eligibilityView(entries: readonly CcAxisEligibility[]): CcPeriodEligibility[] {
  return CC_AXES.flatMap(axis => {
    const entry = entries.find(e => e.axis === axis);
    return entry ? [{ axis, label: axisText(axis), eligible: entry.eligible }] : [];
  });
}

function ineligibleReasons(entries: readonly CcAxisEligibility[]): { axis: string; reason: string }[] {
  return entries
    .filter(entry => !entry.eligible)
    .map(entry => ({ axis: axisText(entry.axis), reason: entry.reason || 'Excluded' }));
}

function boundLabels(noun: string, id: number): Record<CcPeriodBound, string> {
  const label = (key: CcPeriodBound) => `Make ${noun} #${id} ${CC_PERIOD_BOUND_LABELS[key]}`;
  return {
    baselineFirstId: label('baselineFirstId'),
    baselineLastId: label('baselineLastId'),
    comparisonFirstId: label('comparisonFirstId'),
    comparisonLastId: label('comparisonLastId')
  };
}

/** The figures of a card after its intelligence: speed, work, cost and answers; `—` without a point. */
function unitMetrics(intelligence: CcPeriodMetric, point: CcTimelinePoint | undefined): CcPeriodMetric[] {
  const streaming = formatTokenRate(point?.medianStreamingRate);
  const work = formatInteger(point?.outputTokensPerAnswer);
  return [
    intelligence,
    { key: 'firstAnswer', label: 'First answer', value: formatMs(point?.medianTimeToFirstAnswerTextMs), note: null },
    {
      key: 'streaming', label: 'Streaming', value: streaming,
      note: point?.streamingRateEstimated && streaming !== NO_VALUE ? 'estimated' : null
    },
    { key: 'work', label: 'Work', value: work, note: work !== NO_VALUE ? 'tokens per answer' : null },
    { key: 'cost', label: 'Cost / question', value: formatUsd(point?.costPerQuestionUsd), note: null },
    { key: 'answers', label: 'Answers', value: formatInteger(point?.answerCount), note: null }
  ];
}

/** `Battery run #12 · suite 1 of 2` on a run that is a battery member; empty otherwise. */
function memberTag(row: CcRunRow): string {
  if (row.batteryRunId === null || row.batteryRunId === undefined) return '';
  const position = typeof row.batterySuitePosition === 'number' && typeof row.batterySuiteCount === 'number'
    ? ` · suite ${row.batterySuitePosition} of ${row.batterySuiteCount}`
    : '';
  return `Battery run #${row.batteryRunId}${position}`;
}

/** `weekday 08–12 UTC, weekend` with ` (estimated)` when the strata are; `—` without. */
function strataText(point: CcTimelinePoint | undefined): string {
  if (!point || point.strata.length === 0) return NO_VALUE;
  return `${point.strata.join(', ')}${point.strataEstimated ? ' (estimated)' : ''}`;
}

/** A run's card, its figures from its timeline point. */
export function runUnitCard(unit: CcPeriodUnit, row: CcRunRow, point: CcTimelinePoint | undefined): CcPeriodUnitCard {
  const id = unit.id;
  return {
    id,
    battery: false,
    title: row.suiteName,
    status: runStatusText(row.status),
    harness: `Harness ${row.harnessVersion ?? '—'}`,
    isLegacy: row.isLegacy,
    isAnchor: row.isAnchor,
    memberTag: memberTag(row),
    startedAtUtc: unit.startedAtUtc,
    started: formatUtcDateTime(unit.startedAtUtc),
    meta: `Served: ${servedModelsText(row.servedModelIds)}`,
    boundsLabel: `Period bounds for run #${id}`,
    boundLabels: boundLabels('run', id),
    reportLabel: `Open the run report of run #${id}`,
    reportTip: 'Open run report',
    eligibilityLabel: `Eligibility of run #${id}`,
    eligibility: eligibilityView(row.eligibility),
    reasons: ineligibleReasons(row.eligibility),
    metrics: unitMetrics(
      { key: 'intelligence', label: 'Intelligence', value: formatFixed(point?.qualityIndex, 1), note: null },
      point
    ),
    segment: segmentText(row),
    telemetry: row.isLegacy ? 'Legacy' : 'Recorded',
    regrade: regradeCoverageText(row),
    controls: controlRunsText(row),
    strata: strataText(point),
    members: [],
    membersLabel: ''
  };
}

/** A battery run's card, its figures from its battery point; the quality is the Overall Index. */
export function batteryUnitCard(
  unit: CcPeriodUnit,
  battery: CcBatteryRunRow,
  point: CcBatteryTimelinePoint | undefined
): CcPeriodUnitCard {
  const id = unit.id;
  const usableSuites = new Set(battery.members.map(member => member.suiteKey || member.suiteName)).size;
  const index = ccNumber(point?.overallIndex);
  const controls = [...new Set(battery.members.flatMap(member => member.matchedControlRunIds))].sort((a, b) => a - b);
  const harnesses = battery.harnessVersions;
  return {
    id,
    battery: true,
    title: battery.batteryName,
    status: batteryRunStatusText(battery.status),
    harness: harnesses.length === 0
      ? 'Harness —'
      : harnesses.length === 1 ? `Harness ${harnesses[0]}` : `Harnesses ${harnesses.join(', ')}`,
    isLegacy: false,
    isAnchor: false,
    memberTag: '',
    startedAtUtc: unit.startedAtUtc,
    started: formatUtcDateTime(unit.startedAtUtc),
    meta: `${usableSuites} of ${plural(battery.suiteCount, 'suite')} · revision ${battery.definitionRevision}`,
    boundsLabel: `Period bounds for battery run #${id}`,
    boundLabels: boundLabels('battery run', id),
    reportLabel: `Open the battery run report of battery run #${id}`,
    reportTip: 'Open battery run report',
    eligibilityLabel: `Eligibility of battery run #${id}`,
    eligibility: eligibilityView(battery.eligibility),
    reasons: ineligibleReasons(battery.eligibility),
    metrics: unitMetrics(
      {
        key: 'intelligence', label: 'Intelligence', value: formatFixed(index, 1),
        note: point && index === null ? point.overallIndexNote || null : null
      },
      point
    ),
    segment: '',
    telemetry: '',
    regrade: '',
    controls: controls.length === 0 ? 'None' : controls.map(control => `#${control}`).join(', '),
    strata: '',
    members: battery.members.map(member => ({
      runId: member.runId,
      text: `#${member.runId} · ${member.suiteName} · ${runStatusText(member.status)} · harness ${member.harnessVersion ?? '—'}`
    })),
    membersLabel: `Member runs of battery run #${id}`
  };
}

/**
 * Step 3's units as one card list, oldest first, the order of the periods: each card carries the
 * four period-bound toggles, its figures, its eligibility and optionally its details, with the
 * composite Overseer changes and the tagged annotations as dividers between the units they fall
 * between. Presentational: the host owns the bounds, the periods and the details toggle, and decides
 * what a pressed bound does. Read-only, it shows a stored analysis's units without the bounds or
 * Split here, under ids of its own so it can share the document with step 3's list.
 */
@Component({
  selector: 'app-cc-period-units',
  standalone: true,
  templateUrl: './period-units.component.html',
  styleUrls: ['./period-units.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcPeriodUnitsComponent implements OnInit {
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  /** Oldest first: also the period order. */
  @Input() units: readonly CcPeriodUnit[] = [];
  /** The four bounds. */
  @Input() ids: CcPeriodIds = CC_NO_PERIOD_IDS;
  /** The period of each unit, by unit id; a unit not in it reads as not used. */
  @Input() assignment: ReadonlyMap<number, CcUnitPeriod> = new Map();
  /** The units are battery runs. */
  @Input() batteryMode = false;
  /** The timeline's run points. */
  @Input() points: readonly CcTimelinePoint[] = [];
  /** The timeline's battery points. */
  @Input() batteryPoints: readonly CcBatteryTimelinePoint[] = [];
  /** The composite Overseer changes, tagged E1…. */
  @Input() eventGroups: readonly CcEventGroup[] = [];
  /** The annotations, tagged A1…. */
  @Input() annotations: readonly CcTaggedAnnotation[] = [];
  /** Show run details. */
  @Input() details = true;
  /** No bounds and no Split here: the periods are given, and only the reports and details remain. */
  @Input() readonly = false;

  @Output() readonly boundChange = new EventEmitter<{ key: CcPeriodBound; unitId: number }>();
  /** An event's group key, or an annotation's id as a string. */
  @Output() readonly splitAt = new EventEmitter<{ kind: 'event' | 'annotation'; key: string }>();
  @Output() readonly openRunReport = new EventEmitter<number>();
  @Output() readonly openBatteryRunReport = new EventEmitter<number>();
  @Output() readonly detailsChange = new EventEmitter<boolean>();

  readonly boundRows = BOUND_ROWS;

  /** The list's rows, kept until the units, the groups or the annotations change. */
  private itemsMemo: {
    units: readonly CcPeriodUnit[];
    groups: readonly CcEventGroup[];
    annotations: readonly CcTaggedAnnotation[];
    items: CcPeriodUnitsItem[];
  } | null = null;

  /** The cards by unit id, kept until the units or either point list change. */
  private cardsMemo: {
    units: readonly CcPeriodUnit[];
    points: readonly CcTimelinePoint[];
    batteryPoints: readonly CcBatteryTimelinePoint[];
    cards: Map<number, CcPeriodUnitCard>;
  } | null = null;

  ngOnInit(): void {
    ensureOverlayPolyfills();
  }

  /** The prefix of the cards' ids and anchor names: `cc-pu`, or `cc-res-pu` while read-only. */
  get idBase(): string {
    return this.readonly ? 'cc-res-pu' : 'cc-pu';
  }

  /** The heading's id, which the list is labeled by. */
  get headingId(): string {
    return this.readonly ? 'cc-res-units-title' : 'cc-an-units-title';
  }

  /** `Runs`, or `Runs in the periods` while read-only; battery runs in a battery list. */
  get headingText(): string {
    const noun = this.batteryMode ? 'Battery runs' : 'Runs';
    return this.readonly ? `${noun} in the periods` : noun;
  }

  get items(): readonly CcPeriodUnitsItem[] {
    const memo = this.itemsMemo;
    if (memo && memo.units === this.units && memo.groups === this.eventGroups && memo.annotations === this.annotations) {
      return memo.items;
    }
    this.itemsMemo = {
      units: this.units,
      groups: this.eventGroups,
      annotations: this.annotations,
      items: periodUnitItems(this.units, this.eventGroups, this.annotations)
    };
    return this.itemsMemo.items;
  }

  private cards(): Map<number, CcPeriodUnitCard> {
    const memo = this.cardsMemo;
    if (memo && memo.units === this.units && memo.points === this.points && memo.batteryPoints === this.batteryPoints) {
      return memo.cards;
    }
    const points = new Map(this.points.map(point => [point.runId, point]));
    const batteryPoints = new Map(this.batteryPoints.map(point => [point.runId, point]));
    const cards = new Map<number, CcPeriodUnitCard>();
    for (const unit of this.units) {
      cards.set(unit.id, unit.battery
        ? batteryUnitCard(unit, unit.battery, batteryPoints.get(unit.id))
        : runUnitCard(unit, unit.runs[0], points.get(unit.id)));
    }
    this.cardsMemo = { units: this.units, points: this.points, batteryPoints: this.batteryPoints, cards };
    return cards;
  }

  cardOf(unit: CcPeriodUnit): CcPeriodUnitCard {
    return this.cards().get(unit.id)!;
  }

  periodOf(id: number): CcUnitPeriod {
    return this.assignment.get(id) ?? 'notUsed';
  }

  periodText(period: CcUnitPeriod): string {
    return PERIOD_TEXT[period];
  }

  onBound(key: CcPeriodBound, unitId: number): void {
    if (this.readonly) return;
    this.boundChange.emit({ key, unitId });
  }

  onSplit(marker: CcPeriodMarker): void {
    if (this.readonly) return;
    this.splitAt.emit({ kind: marker.kind, key: marker.splitKey });
  }

  onReport(card: CcPeriodUnitCard): void {
    if (card.battery) this.openBatteryRunReport.emit(card.id);
    else this.openRunReport.emit(card.id);
  }

  /** Scrolls the unit's card into view (block 'nearest'; smooth only without prefers-reduced-motion) and focuses its title. */
  focusUnit(id: number): void {
    const title = this.host.nativeElement.querySelector<HTMLElement>(`#${this.idBase}-${id}-title`);
    if (!title) return;
    const card = title.closest<HTMLElement>('.cc-pu-card') ?? title;
    card.scrollIntoView({ block: 'nearest', behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
    title.focus({ preventScroll: true });
  }
}
