import { ChangeDetectionStrategy, Component, Input, OnChanges, SimpleChanges } from '@angular/core';

import { NO_VALUE, endpointMdeText, gradeText, plural } from '../../chat-consistency-format';
import {
  CcEndpointStatus,
  CcEstimateParts,
  CcIntervalGeometry,
  ccEndpointNotes,
  ccEndpointStatus,
  ccEndpointStatusText,
  ccEstimateParts,
  ccIntervalGeometry
} from '../../chat-consistency-results';
import { CcCheck, CcCheckStatus, CcEndpointResult } from '../../chat-consistency.models';

/** At most this many notes show on the card; the rest are under *More about …*. */
export const CC_EP_VISIBLE_NOTES = 2;

/** The visible word of a robustness check's status. */
export const CC_CHECK_STATUS_TEXT: Readonly<Record<CcCheckStatus, string>> = Object.freeze({
  passed: 'Passed',
  failed: 'Failed',
  notAssessable: 'Not assessable'
});

/** One note of the card. */
export interface CcEndpointCardNote {
  text: string;
  /** The minimum-sample shortfall, shown first with a warning icon. */
  shortfall: boolean;
}

/** One fact of the card's list. */
export interface CcEndpointCardFact {
  key: 'meaning' | 'mde' | 'compared';
  term: string;
  value: string;
}

/** The interval bar's end captions: the direction of each end. */
export interface CcIntervalEnds {
  start: string;
  end: string;
}

const SHORTFALL_PREFIX = /^below the minimum sample:/i;

/**
 * One computed endpoint of the Results step: its id, name, margin, status and grade, the estimate with
 * its 95 % interval and the interval bar against the margin, what the verdict means, how much was
 * compared, and its notes, two of them shown and the rest with the robustness checks in a disclosure.
 */
@Component({
  selector: 'app-cc-endpoint-card',
  standalone: true,
  templateUrl: './endpoint-card.component.html',
  styleUrls: ['./endpoint-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcEndpointCardComponent implements OnChanges {
  @Input({ required: true }) endpoint!: CcEndpointResult;
  /** The analysis compared battery runs; the run counts are then their member runs. */
  @Input() batteryAnalysis = false;

  status: CcEndpointStatus = 'notComputable';
  statusText = '';
  parts: CcEstimateParts = { estimate: '', interval: null };
  geometry: CcIntervalGeometry | null = null;
  ends: CcIntervalEnds = { start: '', end: '' };
  facts: CcEndpointCardFact[] = [];
  visibleNotes: CcEndpointCardNote[] = [];
  moreNotes: CcEndpointCardNote[] = [];

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['endpoint'] || changes['batteryAnalysis']) && this.endpoint) {
      const endpoint = this.endpoint;
      this.status = ccEndpointStatus(endpoint);
      this.statusText = ccEndpointStatusText(endpoint);
      this.parts = ccEstimateParts(endpoint);
      this.geometry = ccIntervalGeometry(endpoint);
      this.ends = intervalEnds(endpoint.direction);

      const { meaning, notes } = ccEndpointNotes(endpoint);
      const facts: CcEndpointCardFact[] = [];
      if (meaning) facts.push({ key: 'meaning', term: 'What it means', value: meaning });
      if (this.status === 'inconclusive') {
        const mde = endpointMdeText(endpoint);
        if (mde !== NO_VALUE) facts.push({ key: 'mde', term: 'Smallest change this sample can detect', value: mde });
      }
      facts.push({ key: 'compared', term: 'Compared', value: this.comparedText(endpoint) });
      this.facts = facts;

      const ordered = [
        ...notes.filter(note => SHORTFALL_PREFIX.test(note)),
        ...notes.filter(note => !SHORTFALL_PREFIX.test(note))
      ].map(text => ({ text, shortfall: SHORTFALL_PREFIX.test(text) }));
      this.visibleNotes = ordered.slice(0, CC_EP_VISIBLE_NOTES);
      this.moreNotes = ordered.slice(CC_EP_VISIBLE_NOTES);
    }
  }

  get cardId(): string {
    return `cc-ep-${this.endpoint.id}`;
  }

  get checks(): readonly CcCheck[] {
    return this.endpoint.robustnessChecks;
  }

  /** The grade, shown only where it says more than *Not established*. */
  get gradeShown(): boolean {
    return this.endpoint.grade !== 'notEstablished';
  }

  get gradeWord(): string {
    return gradeText(this.endpoint.grade);
  }

  /** What the disclosure holds: the notes past the first two, and the robustness checks. */
  get moreCount(): number {
    return this.moreNotes.length + this.checks.length;
  }

  checkStatusText(status: CcCheckStatus): string {
    return CC_CHECK_STATUS_TEXT[status] ?? status;
  }

  /** `3 runs vs 3 runs · 60 paired items`; in a battery analysis the member runs. */
  private comparedText(endpoint: CcEndpointResult): string {
    const noun = this.batteryAnalysis ? 'member run' : 'run';
    const runs = `${plural(endpoint.baselineRunCount, noun)} vs ${plural(endpoint.comparisonRunCount, noun)}`;
    return endpoint.itemCount > 0 ? `${runs} · ${plural(endpoint.itemCount, 'paired item')}` : runs;
  }
}

/** The direction each end of the interval bar points: worse and better, or less and more work. */
export function intervalEnds(direction: string): CcIntervalEnds {
  switch (direction) {
    case 'higherIsBetter':
      return { start: 'worse', end: 'better' };
    case 'lowerIsBetter':
      return { start: 'better', end: 'worse' };
    case 'work':
      return { start: 'less work', end: 'more work' };
    default:
      return { start: '', end: '' };
  }
}
