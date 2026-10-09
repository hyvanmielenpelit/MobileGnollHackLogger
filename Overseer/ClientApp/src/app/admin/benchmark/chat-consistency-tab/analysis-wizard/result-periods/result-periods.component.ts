import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { CcEventGroup, CcTaggedAnnotation } from '../../chat-consistency-events';
import { NO_VALUE, formatInteger, formatUtcDate, plural } from '../../chat-consistency-format';
import { CcPeriod, CcPeriodBound } from '../../chat-consistency-periods';
import { CcResultPeriodUnits, ccResultPeriodUnits } from '../../chat-consistency-results';
import {
  CcAnalysisResult,
  CcBatteryRunRow,
  CcBatteryTimelinePoint,
  CcPeriodSummary,
  CcRunRow,
  CcTimelinePoint
} from '../../chat-consistency.models';
import { CcPeriodUnitsComponent } from '../period-units/period-units.component';

/** A bound of a period card's range: the unit whose report it opens. */
export interface CcResultPeriodBound {
  id: number;
  /** `First: battery run #11`, `Last: run #96`, or `First and last: run #96` for a one-unit period. */
  text: string;
  /** The text, then what the button opens. */
  label: string;
}

/** One fact of a period card. */
export interface CcResultPeriodFact {
  key: 'batteryRuns' | 'runs' | 'days' | 'answers' | 'items' | 'suites' | 'legacy';
  term: string;
  value: string;
}

/** One period card: the stored period's dates, range and sample. */
export interface CcResultPeriodCard {
  period: CcPeriod;
  name: string;
  titleId: string;
  /** yyyy-MM-dd, UTC. */
  startDay: string;
  /** yyyy-MM-dd, UTC; null when the period starts and ends on one day. */
  endDay: string | null;
  /** The first and last found unit, one entry when they are the same; empty when none was found. */
  bounds: CcResultPeriodBound[];
  facts: CcResultPeriodFact[];
}

/**
 * The Results step's periods, read from the stored analysis: a card per period with its dates, the
 * range of its units (each opening that unit's report) and its sample, then step 3's unit cards,
 * read-only, with the units resolved against the loaded rows. Presentational: the host opens the reports.
 */
@Component({
  selector: 'app-cc-result-periods',
  standalone: true,
  imports: [CcPeriodUnitsComponent],
  templateUrl: './result-periods.component.html',
  styleUrls: ['./result-periods.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcResultPeriodsComponent {
  @Input({ required: true }) result!: CcAnalysisResult;
  /** Every run step 1 loaded, battery members included. */
  @Input() rows: readonly CcRunRow[] = [];
  /** Every battery run step 1 loaded. */
  @Input() batteryRows: readonly CcBatteryRunRow[] = [];
  /** The timeline's run points. */
  @Input() points: readonly CcTimelinePoint[] = [];
  /** The timeline's battery points. */
  @Input() batteryPoints: readonly CcBatteryTimelinePoint[] = [];
  /** The composite Overseer changes, tagged E1…. */
  @Input() eventGroups: readonly CcEventGroup[] = [];
  /** The annotations, tagged A1…. */
  @Input() annotations: readonly CcTaggedAnnotation[] = [];

  @Output() readonly openRunReport = new EventEmitter<number>();
  @Output() readonly openBatteryRunReport = new EventEmitter<number>();

  /** Show run details on the unit cards; off on every visit. */
  details = false;

  /** The resolved units, kept until the result or either row list changes. */
  private unitsMemo: {
    result: CcAnalysisResult;
    rows: readonly CcRunRow[];
    batteryRows: readonly CcBatteryRunRow[];
    view: CcResultPeriodUnits;
  } | null = null;

  /** The period cards, kept until the result or the resolved units change. */
  private cardsMemo: { result: CcAnalysisResult; view: CcResultPeriodUnits; cards: CcResultPeriodCard[] } | null = null;

  get batteryAnalysis(): boolean {
    return this.result.unitKind === 'batteryRun';
  }

  /** `run`, or `battery run` in a battery analysis. */
  get noun(): string {
    return this.batteryAnalysis ? 'battery run' : 'run';
  }

  /** The stored units resolved against the rows, with their stored periods and the count not found. */
  get unitView(): CcResultPeriodUnits {
    const memo = this.unitsMemo;
    if (memo && memo.result === this.result && memo.rows === this.rows && memo.batteryRows === this.batteryRows) {
      return memo.view;
    }
    const view = ccResultPeriodUnits(this.result, this.rows, this.batteryRows);
    this.unitsMemo = { result: this.result, rows: this.rows, batteryRows: this.batteryRows, view };
    return view;
  }

  get periods(): readonly CcResultPeriodCard[] {
    const view = this.unitView;
    const memo = this.cardsMemo;
    if (memo && memo.result === this.result && memo.view === view) return memo.cards;
    const cards = [
      this.periodCard('baseline', 'Baseline', this.result.baseline, 'baselineFirstId', 'baselineLastId', view),
      this.periodCard('comparison', 'Comparison', this.result.comparison, 'comparisonFirstId', 'comparisonLastId', view)
    ];
    this.cardsMemo = { result: this.result, view, cards };
    return cards;
  }

  /** `2 runs of this analysis are not among the runs step 1 loaded. …`; empty when every unit was found. */
  get missingText(): string {
    const missing = this.unitView.missing;
    if (missing === 0) return '';
    const [verb, pronoun] = missing === 1 ? ['is', 'it'] : ['are', 'them'];
    return `${plural(missing, this.noun)} of this analysis ${verb} not among the ${this.noun}s step 1 loaded. `
      + `Widen step 1's dates to show ${pronoun}.`;
  }

  openUnit(id: number): void {
    if (this.batteryAnalysis) this.openBatteryRunReport.emit(id);
    else this.openRunReport.emit(id);
  }

  onDetailsChange(details: boolean): void {
    this.details = details;
  }

  private periodCard(
    period: CcPeriod,
    name: string,
    summary: CcPeriodSummary,
    firstKey: CcPeriodBound,
    lastKey: CcPeriodBound,
    view: CcResultPeriodUnits
  ): CcResultPeriodCard {
    const startDay = formatUtcDate(summary.startUtc);
    const endDay = formatUtcDate(summary.endUtc);
    return {
      period,
      name,
      titleId: `cc-rp-${period}-title`,
      startDay,
      endDay: endDay === startDay ? null : endDay,
      bounds: this.bounds(view.ids[firstKey], view.ids[lastKey]),
      facts: this.facts(period, summary)
    };
  }

  private bounds(firstId: number | null, lastId: number | null): CcResultPeriodBound[] {
    const report = this.batteryAnalysis ? 'battery run report' : 'run report';
    const bound = (word: string, id: number): CcResultPeriodBound => {
      const text = `${word}: ${this.noun} #${id}`;
      return { id, text, label: `${text}, open the ${report}` };
    };
    if (firstId !== null && firstId === lastId) return [bound('First and last', firstId)];
    return [
      ...(firstId !== null ? [bound('First', firstId)] : []),
      ...(lastId !== null ? [bound('Last', lastId)] : [])
    ];
  }

  private facts(period: CcPeriod, summary: CcPeriodSummary): CcResultPeriodFact[] {
    const facts: CcResultPeriodFact[] = [];
    if (this.batteryAnalysis && this.result.units && this.result.units.length > 0) {
      const count = this.result.units.filter(unit => unit.kind === 'batteryRun' && unit.period === period).length;
      facts.push({ key: 'batteryRuns', term: 'Battery runs', value: formatInteger(count) });
    }
    facts.push(
      { key: 'runs', term: 'Runs', value: formatInteger(summary.runCount) },
      { key: 'days', term: 'Days', value: formatInteger(summary.days.length) },
      { key: 'answers', term: 'Answers', value: formatInteger(summary.answerCount) },
      { key: 'items', term: 'Items', value: formatInteger(summary.itemCount) },
      { key: 'suites', term: 'Suites', value: summary.suiteNames.length > 0 ? summary.suiteNames.join(', ') : NO_VALUE }
    );
    if (summary.legacyRunCount > 0) {
      facts.push({ key: 'legacy', term: 'Runs without telemetry', value: formatInteger(summary.legacyRunCount) });
    }
    return facts;
  }
}
