import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

import { ProviderBadgeComponent } from '../../../../shared/provider-badge/provider-badge.component';
import { formatServiceTier, formatThinkingLevel } from '../../../../utils/model-badge-format.util';
import { NO_VALUE, formatInteger, formatUtcDate, plural } from '../chat-consistency-format';
import { CcAnalysisSummary, CcModelAxis } from '../chat-consistency.models';

/**
 * The launcher's *Current model* card: the model chosen in the wizard, its runs and dates, and its
 * newest saved analysis, with the latest run report and that analysis one click away. A read-out,
 * not a control: the wizard owns the choice.
 */
@Component({
  selector: 'app-cc-current-model-card',
  standalone: true,
  imports: [ProviderBadgeComponent],
  templateUrl: './current-model-card.component.html',
  styleUrls: ['./current-model-card.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CcCurrentModelCardComponent {
  @Input({ required: true }) axis!: CcModelAxis;
  /** The chosen dates as the wizard names them: `All dates`, `Last 7 days`, `2026-09-01 to 2026-10-05`. */
  @Input() rangeText = '';
  /** The runs in the chosen dates; null when every date is chosen or the timeline is not loaded. */
  @Input() runsInRange: number | null = null;
  /** The runs the analysis uses; null when step 1 does not narrow them. */
  @Input() runsInAnalysis: number | null = null;
  @Input() latestAnalysis: CcAnalysisSummary | null = null;
  /** The timeline and the runs of the model are being read. */
  @Input() loading = false;

  /** The run report of the model's latest run, by run id. */
  @Output() readonly openRunReport = new EventEmitter<number>();
  /** A saved analysis in the wizard, by id. */
  @Output() readonly openAnalysis = new EventEmitter<number>();

  get thinkingText(): string {
    return formatThinkingLevel(this.axis.thinkingLevel);
  }

  get tierText(): string {
    return formatServiceTier(this.axis.serviceTier);
  }

  /** `6 runs · 4 with call telemetry`. */
  get runsText(): string {
    return `${plural(this.axis.runCount, 'run')} · ${formatInteger(this.axis.telemetryRunCount)} with call telemetry`;
  }

  /** `· 3 runs in these dates` after the range text, or empty when every date is chosen. */
  get runsInRangeText(): string {
    return this.runsInRange === null ? '' : `· ${plural(this.runsInRange, 'run')} in these dates`;
  }

  get runsInAnalysisText(): string {
    return this.runsInAnalysis === null ? '' : plural(this.runsInAnalysis, 'run');
  }

  get firstRunText(): string {
    return formatUtcDate(this.axis.firstRunAtUtc);
  }

  /** `#106 · 2026-10-01`. */
  get latestRunText(): string {
    return `#${this.axis.latestRunId} · ${formatUtcDate(this.axis.lastRunAtUtc)}`;
  }

  get suitesText(): string {
    return this.axis.suiteNames.length > 0 ? this.axis.suiteNames.join(', ') : NO_VALUE;
  }

  /** `#7 · Analysis 7 · saved 2026-10-02`. */
  latestAnalysisText(analysis: CcAnalysisSummary): string {
    return `#${analysis.id} · ${analysis.name} · saved ${formatUtcDate(analysis.createdAtUtc)}`;
  }
}
