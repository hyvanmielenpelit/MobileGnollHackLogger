import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { formatUtcDateTime, plural } from '../../chat-consistency-format';
import {
  CC_NO_PERIOD_IDS,
  CC_PERIOD_BOUND_LABELS,
  CcPeriod,
  CcPeriodBound,
  CcPeriodIds,
  CcPeriodUnit,
  CcPeriodWindows
} from '../../chat-consistency-periods';
import {
  CcPeriodSample,
  ccPeriodSample,
  ccSampleCountText,
  ccSampleDayText,
  ccSampleNeedText
} from '../../chat-consistency-readiness';

/** One bound of a period's range as the strip shows it. */
export interface CcPsBound {
  key: CcPeriodBound;
  id: number | null;
  /** `Go to run #104, the comparison's first run`; empty while unset. */
  label: string;
}

/** One period card of the strip. */
export interface CcPsPeriod {
  period: CcPeriod;
  name: string;
  bounds: [CcPsBound, CcPsBound];
  sample: CcPeriodSample;
  /** `3 runs on 2 days · 2026-10-01 to 2026-10-03`. */
  countText: string;
  /** `Window 2026-10-01 00:00 to 2026-10-03 23:59 UTC`; empty without windows. */
  windowText: string;
}

/**
 * The Analyze step's period summary: each period's range, sample and window, the units in neither
 * period, and the preview's readiness, kept in view above the period cards. A read-out: its links move
 * to a unit's card or to the preview, and the host owns every choice.
 */
@Component({
  selector: 'app-cc-period-summary',
  standalone: true,
  templateUrl: './period-summary.component.html',
  styleUrls: ['./period-summary.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcPeriodSummaryComponent {
  @Input() batteryMode = false;
  @Input() ids: CcPeriodIds = CC_NO_PERIOD_IDS;
  /** Why the periods cannot be analyzed; '' when they can. */
  @Input() refusal = '';
  /** The eligible units of each period (empty while refused). */
  @Input() baseline: readonly CcPeriodUnit[] = [];
  @Input() comparison: readonly CcPeriodUnit[] = [];
  /** The windows the request will carry; null while refused. */
  @Input() windows: CcPeriodWindows | null = null;
  /** Eligible units in neither period. */
  @Input() notUsedCount = 0;
  /** The preview's notes; 0 is ready. */
  @Input() noteCount = 0;

  /** A unit's card, by unit id. */
  @Output() readonly goToUnit = new EventEmitter<number>();
  @Output() readonly openPreview = new EventEmitter<void>();

  get noun(): string {
    return this.batteryMode ? 'battery run' : 'run';
  }

  get periods(): CcPsPeriod[] {
    return [
      this.periodView('baseline', 'Baseline', 'baselineFirstId', 'baselineLastId', this.baseline,
        this.windows?.baselineStartUtc, this.windows?.baselineEndUtc),
      this.periodView('comparison', 'Comparison', 'comparisonFirstId', 'comparisonLastId', this.comparison,
        this.windows?.comparisonStartUtc, this.windows?.comparisonEndUtc)
    ];
  }

  /** `Not used: 2 runs outside the periods`; empty at 0. */
  get notUsedText(): string {
    return this.notUsedCount > 0 ? `Not used: ${plural(this.notUsedCount, this.noun)} outside the periods` : '';
  }

  /** `Preview: 3 notes`, or `Preview: ready`. */
  get previewText(): string {
    return this.noteCount > 0 ? `Preview: ${plural(this.noteCount, 'note')}` : 'Preview: ready';
  }

  get needText(): string {
    return ccSampleNeedText();
  }

  goTo(bound: CcPsBound): void {
    if (bound.id !== null) this.goToUnit.emit(bound.id);
  }

  private periodView(
    period: CcPeriod,
    name: string,
    firstKey: CcPeriodBound,
    lastKey: CcPeriodBound,
    units: readonly CcPeriodUnit[],
    startUtc: string | undefined,
    endUtc: string | undefined
  ): CcPsPeriod {
    const sample = ccPeriodSample(units);
    const days = ccSampleDayText(sample);
    return {
      period,
      name,
      bounds: [this.bound(firstKey), this.bound(lastKey)],
      sample,
      countText: days ? `${ccSampleCountText(sample, this.batteryMode)} · ${days}` : ccSampleCountText(sample, this.batteryMode),
      windowText: startUtc && endUtc
        ? `Window ${formatUtcDateTime(startUtc).replace(/ UTC$/, '')} to ${formatUtcDateTime(endUtc)}`
        : ''
    };
  }

  private bound(key: CcPeriodBound): CcPsBound {
    const id = this.ids[key];
    return { key, id, label: id === null ? '' : `Go to ${this.noun} #${id}, ${CC_PERIOD_BOUND_LABELS[key]}` };
  }
}
